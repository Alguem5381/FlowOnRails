using System.Collections.Immutable;
using FlowOnRails.AspNetCore.Translation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FlowOnRails.AspNetCore.Generators.Tests;

public class EndpointMappingAnalyzerTests
{
    private const string Imports = """
        using System;
        using System.Threading.Tasks;
        using FlowOnRails;
        using FlowOnRails.AspNetCore.Translation;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.Extensions.DependencyInjection;
        """;
    private const string Domain = """
        public record Parent : Error;
        public sealed record Child : Parent;
        public sealed record Other : Error;
        public static class Handlers {
            public static Rail Read() => new Child();
            public static Task<Rail> ReadAsync() => Task.FromResult<Rail>(new Child());
            public static ValueTask<Rail> ReadValueAsync() => new(new Child());
        }
        """;

    [Fact]
    public async Task ExactVisibleErrorWithoutMapping_WarnsAtEndpoint()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            """);
        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
    }

    [Theory]
    [InlineData("scope => scope.For<Child>().UseGenericProblem()")]
    [InlineData("scope => scope.ForTree<Parent>(tree => tree.UseGenericProblem())")]
    public async Task EndpointGenericOverride_PrecedesGlobalTranslation(string visibility)
    {
        var diagnostics = await Analyze($$"""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.ForTree<Error>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok())).WithErrorVisibility({{visibility}});
            """);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("Handlers.Read()")]
    [InlineData("(await Handlers.ReadAsync())")]
    [InlineData("(await Handlers.ReadValueAsync())")]
    public async Task ExactManualMapping_SatisfiesSyncAndAwaitedForwarding(string handler)
    {
        var async = handler.Contains("await", StringComparison.Ordinal) ? "async " : "";
        var diagnostics = await Analyze($$"""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsTranslation(c => c.Add<Child>(422, p => p.Title("Child")));
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.ForTree<Parent>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", {{async}}() => {{handler}}.ToHttpResult(() => Results.Ok()));
            """);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task RegisteredGeneratedCatalog_SuppliesOnlyItsExactMaps()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddProblems();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.ForTree<Parent>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            """, """
            public static partial class Problems {
                [HttpProblem(422)] private static void Map(ProblemBuilder<Child> p) { }
            }
            """);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("p.Global.For<Parent>().TranslateEachType();", false)]
    [InlineData("p.Global.For<Parent>().UseGenericProblem();", true)]
    public async Task AncestorMapping_RequiresAuthorizationAndHonorsGenericBoundary(string parentRule, bool missing)
    {
        var diagnostics = await Analyze($$"""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsTranslation(c => c.Add<Parent>(422, p => p.Title("Parent")));
            builder.Services.AddFlowOnRailsVisibility(p => {
                {{parentRule}}
                p.Global.For<Child>().UseNearestAncestorMapping();
            });
            var app = builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            """);
        Assert.Equal(missing ? 1 : 0, diagnostics.Count(item => item.Id == "FORHTTP009"));
    }

    [Fact]
    public async Task NestedRouteGroups_UseInnerThenEndpointOverrides()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            var app = builder.Build();
            var outer = app.MapGroup("/outer").WithErrorVisibility(s => s.For<Child>().UseGenericProblem());
            var inner = outer.MapGroup("/inner").WithErrorVisibility(s => s.For<Child>().TranslateEachType());
            inner.MapGet("/a", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            inner.MapGet("/b", () => Handlers.Read().ToHttpResult(() => Results.Ok()))
                .WithErrorVisibility(s => s.For<Child>().UseGenericProblem());
            """);
        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
    }

    [Fact]
    public async Task UnknownControllerScope_DoesNotBlockPeerController()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers();
            builder.Services.AddFlowOnRailsVisibility(p => {
                p.ForController<FirstController>(s => Configure(s));
                p.ForController<SecondController>(s => s.For<Child>().TranslateEachType());
            });
            var app = builder.Build();
            app.MapControllers();
            static void Configure(FlowOnRails.AspNetCore.Visibility.RuleScope s) { }
            """, """
            public class FirstController : ControllerBase {
                public IActionResult Get() => Handlers.Read().ToActionResult(() => Ok());
            }
            public class SecondController : ControllerBase {
                public IActionResult Get() => Handlers.Read().ToActionResult(() => Ok());
            }
            """);
        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Fact]
    public async Task LocalRailStorage_ChecksValueAtConversion()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => { var rail = Handlers.Read(); return rail.ToHttpResult(() => Results.Ok()); });
            """);
        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
        Assert.DoesNotContain(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Fact]
    public async Task DifferentApplications_DoNotShareMappingsOrPolicies()
    {
        var diagnostics = await Analyze("""
            var first = WebApplication.CreateBuilder();
            first.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var firstApp = first.Build();
            firstApp.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            var second = WebApplication.CreateBuilder();
            second.Services.AddFlowOnRailsTranslation(c => c.Add<Child>(422, p => p.Title("Child")));
            var secondApp = second.Build();
            secondApp.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            """);
        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
    }

    [Fact]
    public async Task MethodGroupHttpWrapper_IsCheckedAsEndpoint()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", Endpoint);
            static IResult Endpoint() => Handlers.Read().ToHttpResult(() => Results.Ok());
            """);
        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
    }

    [Fact]
    public async Task UnregisteredConverterHelper_IsNotAnEndpoint()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            """, "public static class Utility { public static IResult Read() => Handlers.Read().ToHttpResult(() => Results.Ok()); }");
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("if (DateTime.Now.Ticks > 0) builder.Services.AddFlowOnRailsTranslation(c => c.Add<Child>(422, p => p.Title(\"Child\")));")]
    [InlineData("Configure(builder.Services); static void Configure(IServiceCollection services) { }")]
    [InlineData("var services = builder.Services; services = new ServiceCollection(); services.AddFlowOnRailsTranslation(c => c.Add<Child>(422, p => p.Title(\"Child\")));")]
    public async Task UnknownRegistration_DoesNotProduceMissingMapClaim(string configuration)
    {
        var diagnostics = await Analyze($$"""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            {{configuration}}
            var app = builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            """);
        Assert.DoesNotContain(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Fact]
    public async Task FrameworkConvention_PreservesEndpointGenericOverride()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok())).WithName("read")
                .WithErrorVisibility(s => s.For<Child>().UseGenericProblem());
            """);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ControllerRouteGenericOverride_PrecedesControllerPolicy()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers();
            builder.Services.AddFlowOnRailsVisibility(p => p.ForController<ReadController>(s => s.For<Child>().TranslateEachType()));
            var app = builder.Build();
            app.MapControllers().WithErrorVisibility(s => s.For<Child>().UseGenericProblem());
            """, """
            public class ReadController : ControllerBase {
                public IActionResult Get() => Handlers.Read().ToActionResult(() => Ok());
            }
            """);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task UnusedNestedConverter_IsNotAnEndpointReturn()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => { Func<IResult> unused = () => Handlers.Read().ToHttpResult(() => Results.Ok()); return Results.Ok(); });
            """);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task RailArgumentTransformation_PreservesKnownInput()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => Forward(Handlers.Read()).ToHttpResult(() => Results.Ok()));
            static Rail Forward(Rail rail) => rail;
            """);
        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
        Assert.DoesNotContain(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Theory]
    [InlineData("p.Global.For<Child>().TranslateEachType(); p.Global.For<Child>().UseGenericProblem();")]
    [InlineData("p.Global.ForTree<Parent>(s => s.For<Other>().TranslateEachType());")]
    public async Task InvalidVisibility_IsUnavailableWithoutMissingMapClaims(string rules)
    {
        var diagnostics = await Analyze($$"""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => { {{rules}} });
            var app = builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            """);
        Assert.DoesNotContain(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Fact]
    public async Task ConditionalControllerConfiguration_DoesNotBlockKnownPeer()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers();
            builder.Services.AddFlowOnRailsVisibility(p => {
                if (DateTime.Now.Ticks > 0) p.ForController<FirstController>(s => s.For<Child>().TranslateEachType());
                p.ForController<SecondController>(s => s.For<Child>().TranslateEachType());
            });
            var app = builder.Build();
            app.MapControllers();
            """, """
            public class FirstController : ControllerBase {
                public IActionResult Get() => Handlers.Read().ToActionResult(() => Ok());
            }
            public class SecondController : ControllerBase {
                public IActionResult Get() => Handlers.Read().ToActionResult(() => Ok());
            }
            """);
        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Fact]
    public async Task HttpForwardingHelper_IsExplicitlyUnavailable()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => Forward());
            static IResult Forward() => Handlers.Read().ToHttpResult(() => Results.Ok());
            """);
        Assert.DoesNotContain(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Fact]
    public async Task UnknownFrameworkConvention_PreservesUncertaintyOfLocalVisibility()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok())).CacheOutput()
                .WithErrorVisibility(s => s.For<Child>().UseGenericProblem());
            """);
        Assert.DoesNotContain(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Theory]
    [InlineData("Action register = () => builder.Services.AddFlowOnRailsTranslation(c => c.Add<Child>(422, p => p.Title(\"Child\")));")]
    [InlineData("void Register() => builder.Services.AddFlowOnRailsTranslation(c => c.Add<Child>(422, p => p.Title(\"Child\")));")]
    public async Task DeferredRegistrations_AreNotTreatedAsInstalledMappings(string registration)
    {
        var diagnostics = await Analyze($$"""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            {{registration}}
            var app = builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            """);
        Assert.DoesNotContain(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Theory]
    [InlineData("public class ReadController")]
    [InlineData("[Controller] public class Reads")]
    public async Task PocoControllerActions_AreChecked(string declaration)
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapControllers();
            """, declaration + """
            { public IActionResult Get() => Handlers.Read().ToActionResult(() => new OkResult()); }
            """);
        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
    }

    [Fact]
    public async Task InheritedControllerAction_RequiresConcreteEndpointContext()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers();
            builder.Services.AddFlowOnRailsVisibility(p => {
                p.ForController<BaseController>(s => s.For<Child>().TranslateEachType());
                p.ForController<DerivedController>(s => s.For<Child>().UseGenericProblem());
            });
            var app = builder.Build();
            app.MapControllers();
            """, """
            public class BaseController : ControllerBase {
                public IActionResult Get() => Handlers.Read().ToActionResult(() => Ok());
            }
            public class DerivedController : BaseController;
            """);
        Assert.DoesNotContain(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Fact]
    public async Task UncalledConfigurationMethod_DoesNotInstallItsCatalog()
    {
        var diagnostics = await Analyze("""
            Setup.Builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = Setup.Builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            """, """
            public static class Setup {
                public static WebApplicationBuilder Builder = WebApplication.CreateBuilder();
                public static void NeverCalled() => Builder.Services.AddFlowOnRailsTranslation(c => c.Add<Child>(422, p => p.Title("Child")));
            }
            """);
        Assert.DoesNotContain(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Theory]
    [InlineData("var rail = Handlers.Read(); rail = rail.RecoverWith(error => Rail.Ok());", false)]
    [InlineData("Rail rail = new Other(); rail = Handlers.Read();", true)]
    [InlineData("var rail = Forward(new Child());", true)]
    public async Task LocalFlow_ChecksOnlyErrorsReachingConversion(string statements, bool missing)
    {
        var diagnostics = await Analyze($$"""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => { {{statements}} return rail.ToHttpResult(() => Results.Ok()); });
            static Rail Forward(Rail value) => value;
            """);

        Assert.Equal(missing ? 1 : 0, diagnostics.Count(item => item.Id == "FORHTTP009"));
        Assert.DoesNotContain(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Fact]
    public async Task BranchConversions_KeepTheirOwnErrorCases()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.ForTree<Error>().TranslateEachType());
            builder.Services.AddFlowOnRailsTranslation(c => c.Add<Other>(422, p => p.Title("Other")));
            var app = builder.Build();
            app.MapGet("/", (bool first) => {
                if (first) {
                    Rail child = new Child();
                    return child.ToHttpResult(() => Results.Ok());
                }
                Rail other = new Other();
                return other.ToHttpResult(() => Results.Ok());
            });
            """);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains("Child", diagnostic.GetMessage());
        Assert.Contains("child.ToHttpResult", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task KnownUnreachableConversion_DoesNotRequireMapping()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => {
                if (false) return Handlers.Read().ToHttpResult(() => Results.Ok());
                return Results.Ok();
            });
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ControllerBlock_TracksRecoveryBeforeConversion()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapControllers();
            """, """
            public class ReadController : ControllerBase {
                public IActionResult Get() {
                    var rail = Handlers.Read();
                    rail = rail.RecoverWith(error => Rail.Ok());
                    return rail.ToActionResult(() => Ok());
                }
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task SharedHandler_UsesEachEndpointVisibility()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/specific", Endpoint);
            app.MapGet("/generic", Endpoint).WithErrorVisibility(s => s.For<Child>().UseGenericProblem());
            static IResult Endpoint() {
                var rail = Handlers.Read();
                return rail.ToHttpResult(() => Results.Ok());
            }
            """);

        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
    }

    [Fact]
    public async Task ConditionalReturn_ChecksConversionInput()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", (bool convert) => convert
                ? Handlers.Read().ToHttpResult(() => Results.Ok()) : Results.Ok());
            """);

        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
    }

    [Fact]
    public async Task UnknownCapturedRail_ReportsIncompleteFlow()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            var rail = Handlers.Read();
            app.MapGet("/", () => rail.ToHttpResult(() => Results.Ok()));
            """);

        Assert.DoesNotContain(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Fact]
    public async Task UnknownEndpointVisibility_DoesNotBlockPeerEndpoint()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/unknown", () => Handlers.Read().ToHttpResult(() => Results.Ok()))
                .WithErrorVisibility(s => Configure(s));
            app.MapGet("/known", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            static void Configure(FlowOnRails.AspNetCore.Visibility.RuleScope scope) { }
            """);

        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Fact]
    public async Task EscapedRouteGroup_DoesNotBlockIndependentRoutes()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            var group = app.MapGroup("/unknown");
            Configure(group);
            group.MapGet("/child", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            app.MapGet("/known", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            static void Configure(Microsoft.AspNetCore.Routing.RouteGroupBuilder group) { }
            """);

        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains(diagnostics, item => item.Id == "FORHTTP010");
    }

    [Fact]
    public async Task SharedSpecificHandler_ReportsEachMissingMapOnce()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/first", Endpoint);
            app.MapGet("/second", Endpoint);
            static IResult Endpoint() => Handlers.Read().ToHttpResult(() => Results.Ok());
            """);

        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
    }

    [Fact]
    public async Task StaticConversionSyntax_QueriesRailArgument()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => RailResultExtensions.ToHttpResult(Handlers.Read(), () => Results.Ok()));
            """);

        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
    }

    [Fact]
    public async Task PartialHandler_UsesImplementationBody()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", Endpoints.Read);
            """, """
            public static partial class Endpoints {
                public static partial IResult Read();
                public static partial IResult Read() => Handlers.Read().ToHttpResult(() => Results.Ok());
            }
            """);

        Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
    }

    [Fact]
    public async Task ConstructedGenericHandler_PreservesErrorTypeArguments()
    {
        var diagnostics = await Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<ErrorFor<int>>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", Endpoint<int>);
            static IResult Endpoint<T>() => Read<T>().ToHttpResult(() => Results.Ok());
            static Rail Read<T>() => new ErrorFor<T>();
            """, "public sealed record ErrorFor<T> : Error;");

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "FORHTTP009");
        Assert.Contains("ErrorFor<int>", diagnostic.GetMessage());
    }

    internal static async Task<ImmutableArray<Diagnostic>> Analyze(string program, string extra = "")
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(new[] { typeof(Error).Assembly.Location, typeof(ProblemBuilder<>).Assembly.Location }).Distinct(StringComparer.Ordinal);
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = new[] { CSharpSyntaxTree.ParseText(Imports + program + Domain + extra, options, "Consumer.cs") };
        var compilation = CSharpCompilation.Create("Consumer", trees, paths.Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ProblemGenerator().AsSourceGenerator()).WithUpdatedParseOptions(options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        Assert.DoesNotContain(generatorDiagnostics, item => item.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), item => item.Severity == DiagnosticSeverity.Error);
        var diagnostics = await output.WithAnalyzers([new EndpointMappingAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, item => item.Id == "AD0001");
        return diagnostics;
    }
}
