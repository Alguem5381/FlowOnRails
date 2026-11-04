using System.Reflection;

using FlowOnRails.AspNetCore.Visibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOnRails.AspNetCore.Tests;

public class VisibilityMinimalApiTests
{
    private abstract record PublicError : Error;
    private record NotFound : PublicError;
    private sealed record InternalNotFound : NotFound;
    private sealed record OtherError : PublicError;
    private sealed class UsersController;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedScopes_HaveStablePrecedenceRegardlessOfConfigurationOrder(bool reverse)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddFlowOnRailsVisibility(visibility =>
            visibility.Global.ForTree<PublicError>().UseGenericProblem());
        await using var app = builder.Build();
        var outer = app.MapGroup("/outer");
        var inner = outer.MapGroup("/inner");
        var endpoint = inner.MapGet("/own", () => "ok");
        inner.MapGet("/inherited", () => "ok");
        outer.MapGet("/inherited", () => "ok");
        app.MapGet("/global", () => "ok");

        void ConfigureOuter() => outer.WithErrorVisibility(scope => scope.ForTree<PublicError>().TranslateEachType());
        void ConfigureInner() => inner.WithErrorVisibility(scope => scope.ForTree<NotFound>().UseNearestAncestorMapping());
        void ConfigureEndpoint() => endpoint.WithErrorVisibility(scope => scope.ForTree<PublicError>().UseGenericProblem());

        if (reverse)
        {
            ConfigureEndpoint();
            ConfigureInner();
            ConfigureOuter();
        }
        else
        {
            ConfigureOuter();
            ConfigureInner();
            ConfigureEndpoint();
        }

        var policy = app.Services.GetRequiredService<VisibilityPolicy>();

        Assert.Empty(Resolve(app, policy, "/outer/inner/own", new InternalNotFound()).TranslationCandidates);
        Assert.Equal(new[] { typeof(PublicError) }, Resolve(app, policy, "/outer/inner/inherited", new InternalNotFound()).TranslationCandidates);
        Assert.Equal(new[] { typeof(InternalNotFound) }, Resolve(app, policy, "/outer/inherited", new InternalNotFound()).TranslationCandidates);
        Assert.Empty(Resolve(app, policy, "/global", new InternalNotFound()).TranslationCandidates);
    }

    [Fact]
    public async Task UnmatchedLocalRules_FallBackThroughGroupsWithoutLeakingToSiblingEndpoints()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddFlowOnRailsVisibility(visibility => visibility.Global.For<NotFound>().TranslateEachType());
        await using var app = builder.Build();
        var group = app.MapGroup("/group")
            .WithErrorVisibility(scope => scope.ForDescendantsOf<NotFound>().UseNearestAncestorMapping());
        group.MapGet("/one", () => "ok")
            .WithErrorVisibility(scope => scope.For<OtherError>().UseGenericProblem());
        group.MapGet("/two", () => "ok");
        app.MapGet("/outside", () => "ok");
        var policy = app.Services.GetRequiredService<VisibilityPolicy>();

        Assert.Equal(new[] { typeof(NotFound) }, Resolve(app, policy, "/group/one", new InternalNotFound()).TranslationCandidates);
        Assert.Equal(new[] { typeof(NotFound) }, Resolve(app, policy, "/group/two", new InternalNotFound()).TranslationCandidates);
        Assert.Empty(Resolve(app, policy, "/outside", new InternalNotFound()).TranslationCandidates);
    }

    [Fact]
    public async Task LocalGenericResponse_IsFinalInsteadOfFallingBackToGlobalTranslation()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddFlowOnRailsVisibility(visibility => visibility.Global.ForTree<PublicError>().TranslateEachType());
        await using var app = builder.Build();
        app.MapGet("/hidden", () => "ok")
            .WithErrorVisibility(scope => scope.ForTree<PublicError>().UseGenericProblem());
        app.MapGet("/visible", () => "ok");
        var policy = app.Services.GetRequiredService<VisibilityPolicy>();

        Assert.Empty(Resolve(app, policy, "/hidden", new InternalNotFound()).TranslationCandidates);
        Assert.Equal(new[] { typeof(InternalNotFound) }, Resolve(app, policy, "/visible", new InternalNotFound()).TranslationCandidates);
    }

    [Fact]
    public async Task EndpointRules_PrecedeControllerRulesAndControllerRulesPrecedeGlobal()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddFlowOnRailsVisibility(visibility =>
        {
            visibility.Global.ForTree<PublicError>().TranslateEachType();
            visibility.ForController<UsersController>(scope => scope.ForTree<PublicError>().UseGenericProblem());
        });
        await using var app = builder.Build();
        var descriptor = new ControllerActionDescriptor { ControllerTypeInfo = typeof(UsersController).GetTypeInfo() };
        app.MapGet("/override", () => "ok")
            .WithMetadata(descriptor)
            .WithErrorVisibility(scope => scope.For<InternalNotFound>().TranslateEachType());
        app.MapGet("/controller", () => "ok").WithMetadata(descriptor);
        var policy = app.Services.GetRequiredService<VisibilityPolicy>();

        Assert.Equal(new[] { typeof(InternalNotFound) }, Resolve(app, policy, "/override", new InternalNotFound()).TranslationCandidates);
        Assert.Empty(Resolve(app, policy, "/controller", new InternalNotFound()).TranslationCandidates);
    }

    [Fact]
    public async Task AncestorSearch_RespectsGenericBoundariesInOuterGroups()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddFlowOnRailsVisibility(visibility => visibility.Global.ForTree<PublicError>().TranslateEachType());
        await using var app = builder.Build();
        var outer = app.MapGroup("/outer")
            .WithErrorVisibility(scope => scope.For<NotFound>().UseGenericProblem());
        outer.MapGet("/leaf", () => "ok")
            .WithErrorVisibility(scope => scope.For<InternalNotFound>().UseNearestAncestorMapping());
        var policy = app.Services.GetRequiredService<VisibilityPolicy>();

        Assert.Empty(Resolve(app, policy, "/outer/leaf", new InternalNotFound()).TranslationCandidates);
    }

    [Fact]
    public async Task EndpointConfiguration_IsAnImmutableSnapshot()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddFlowOnRailsVisibility(_ => { });
        await using var app = builder.Build();
        VisibilityScope? retainedScope = null;
        app.MapGet("/snapshot", () => "ok").WithErrorVisibility(scope =>
        {
            retainedScope = scope;
            scope.ForTree<PublicError>().TranslateEachType();
        });
        retainedScope!.For<InternalNotFound>().UseGenericProblem();
        var policy = app.Services.GetRequiredService<VisibilityPolicy>();

        Assert.Equal(new[] { typeof(InternalNotFound) }, Resolve(app, policy, "/snapshot", new InternalNotFound()).TranslationCandidates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateConfiguration_ForTheSameEndpointOrGroup_IsRejected(bool group)
    {
        var builder = WebApplication.CreateSlimBuilder();
        await using var app = builder.Build();
        if (group)
        {
            var routes = app.MapGroup("/group");
            routes.WithErrorVisibility(scope => scope.For<NotFound>().TranslateEachType());
            routes.WithErrorVisibility(scope => scope.For<OtherError>().UseGenericProblem());
            routes.MapGet("/leaf", () => "ok");
        }
        else
        {
            var route = app.MapGet("/leaf", () => "ok");
            route.WithErrorVisibility(scope => scope.For<NotFound>().TranslateEachType());
            route.WithErrorVisibility(scope => scope.For<OtherError>().UseGenericProblem());
        }

        Assert.Throws<InvalidOperationException>(() => Endpoints(app));
    }

    [Fact]
    public async Task InvalidConfiguration_IsRejectedBeforeAttachingMetadata()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddFlowOnRailsVisibility(_ => { });
        await using var app = builder.Build();
        var endpoint = app.MapGet("/leaf", () => "ok");

        Assert.Throws<ArgumentNullException>(() => endpoint.WithErrorVisibility(null!));
        Assert.Throws<InvalidOperationException>(() => endpoint.WithErrorVisibility(scope => scope.For<NotFound>()));
        Assert.Throws<InvalidOperationException>(() => endpoint.WithErrorVisibility(scope =>
        {
            scope.ForTree<PublicError>().UseGenericProblem();
            scope.For<NotFound>().TranslateEachType();
        }));
        endpoint.WithErrorVisibility(scope => scope.For<NotFound>().TranslateEachType());
        var policy = app.Services.GetRequiredService<VisibilityPolicy>();

        Assert.Equal(new[] { typeof(NotFound) }, Resolve(app, policy, "/leaf", new NotFound()).TranslationCandidates);
    }

    [Fact]
    public async Task IndependentApplications_DoNotSharePoliciesOrEndpointConfiguration()
    {
        var firstBuilder = WebApplication.CreateSlimBuilder();
        firstBuilder.Services.AddFlowOnRailsVisibility(_ => { });
        var secondBuilder = WebApplication.CreateSlimBuilder();
        secondBuilder.Services.AddFlowOnRailsVisibility(_ => { });
        await using var first = firstBuilder.Build();
        await using var second = secondBuilder.Build();
        first.MapGet("/leaf", () => "ok")
            .WithErrorVisibility(scope => scope.ForTree<PublicError>().TranslateEachType());
        second.MapGet("/leaf", () => "ok")
            .WithErrorVisibility(scope => scope.ForTree<PublicError>().UseGenericProblem());
        var firstPolicy = first.Services.GetRequiredService<VisibilityPolicy>();
        var secondPolicy = second.Services.GetRequiredService<VisibilityPolicy>();

        Assert.NotSame(firstPolicy, secondPolicy);
        Assert.Equal(new[] { typeof(InternalNotFound) }, Resolve(first, firstPolicy, "/leaf", new InternalNotFound()).TranslationCandidates);
        Assert.Empty(Resolve(second, secondPolicy, "/leaf", new InternalNotFound()).TranslationCandidates);
    }

    private static VisibilityResolution
        Resolve(
            WebApplication app,
            VisibilityPolicy policy,
            string route,
            Error error
        )
    {
        var endpoint = Assert.Single(Endpoints(app), endpoint => endpoint.RoutePattern.RawText == route);
        var context = new DefaultHttpContext { RequestServices = app.Services };

        context.SetEndpoint(endpoint);

        return policy.ResolveForRequest(error, context);
    }

    private static RouteEndpoint[] Endpoints(WebApplication app) =>
        [.. ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()];
}
