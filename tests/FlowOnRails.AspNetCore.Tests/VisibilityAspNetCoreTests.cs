using System.Reflection;

using FlowOnRails.AspNetCore.Visibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOnRails.AspNetCore.Tests;

public class VisibilityAspNetCoreTests
{
    private abstract record PublicError : Error;
    private record NotFound : PublicError;
    private sealed record InternalNotFound : NotFound
    {
        public string InternalDetails => throw new InvalidOperationException("Visibility must not read error properties.");
        public override string ToString() => throw new InvalidOperationException("Visibility must not format the error.");
    }
    private sealed class UsersController : ControllerBase;
    private sealed class AdminController : ControllerBase;

    [Fact]
    public void Registration_ProvidesAnImmutableSingletonAndValidatesBeforeAddingServices()
    {
        var services = new ServiceCollection();
        VisibilityBuilder? retainedBuilder = null;
        services.AddFlowOnRailsVisibility(visibility =>
        {
            retainedBuilder = visibility;
            visibility.Global.ForTree<PublicError>().TranslateEachType();
        });
        using var provider = services.BuildServiceProvider(validateScopes: true);
        var policy = provider.GetRequiredService<VisibilityPolicy>();
        using var scope = provider.CreateScope();
        Assert.Same(policy, scope.ServiceProvider.GetRequiredService<VisibilityPolicy>());
        retainedBuilder!.Global.For<InternalNotFound>().UseGenericProblem();
        Assert.Equal(new[] { typeof(InternalNotFound) }, policy.Resolve(typeof(InternalNotFound)).TranslationCandidates);

        var invalidServices = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => invalidServices.AddFlowOnRailsVisibility(visibility => visibility.Global.For<NotFound>()));
        Assert.DoesNotContain(invalidServices, descriptor => descriptor.ServiceType == typeof(VisibilityPolicy));
        Assert.Throws<InvalidOperationException>(() => services.AddFlowOnRailsVisibility(_ => { }));
        Assert.Throws<ArgumentNullException>(() => services.AddFlowOnRailsVisibility(null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestResolution_UsesTheSelectedControllerWithoutReadingErrorProperties(bool admin)
    {
        var services = new ServiceCollection();
        services.AddFlowOnRailsVisibility(visibility =>
        {
            visibility.Global.For<NotFound>().TranslateEachType();
            visibility.Global.ForDescendantsOf<NotFound>().UseNearestAncestorMapping();
            visibility.ForController<UsersController>(local => local.ForTree<PublicError>().UseGenericProblem());
        });
        using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        var controllerType = admin ? typeof(AdminController) : typeof(UsersController);
        var descriptor = new ControllerActionDescriptor { ControllerTypeInfo = controllerType.GetTypeInfo() };
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(descriptor), "selected action"));
        var result = provider.GetRequiredService<VisibilityPolicy>().ResolveForRequest(new InternalNotFound(), context);
        Assert.Equal(admin ? new[] { typeof(NotFound) } : Array.Empty<Type>(), result.TranslationCandidates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestsWithoutControllerMetadata_UseOnlyGlobalRules(bool hasEndpoint)
    {
        var builder = new VisibilityBuilder();
        builder.Global.For<NotFound>().TranslateEachType();
        builder.Global.ForDescendantsOf<NotFound>().UseNearestAncestorMapping();
        builder.ForController<UsersController>(local => local.ForTree<PublicError>().UseGenericProblem());
        var context = new DefaultHttpContext();
        if (hasEndpoint) context.SetEndpoint(new Endpoint(null, EndpointMetadataCollection.Empty, "minimal endpoint"));
        Assert.Equal(new[] { typeof(NotFound) }, builder.Build().ResolveForRequest(new InternalNotFound(), context).TranslationCandidates);
    }

    [Fact]
    public void NullRequestArguments_AreRejected()
    {
        var policy = new VisibilityBuilder().Build();
        Assert.Throws<ArgumentNullException>(() => policy.ResolveForRequest(null!, new DefaultHttpContext()));
        Assert.Throws<ArgumentNullException>(() => policy.ResolveForRequest(new InternalNotFound(), null!));
    }
}
