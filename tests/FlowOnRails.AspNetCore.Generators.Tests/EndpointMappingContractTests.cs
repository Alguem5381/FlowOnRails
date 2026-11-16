namespace FlowOnRails.AspNetCore.Generators.Tests;

public class EndpointMappingContractTests
{
    [Theory]
    [InlineData("public static IServiceCollection Unused => Builder.Services.AddFlowOnRailsTranslation(c => c.Add<Child>(422, p => p.Title(\"Child\")));")]
    [InlineData("public static IServiceCollection Unused { get { Builder.Services.AddFlowOnRailsTranslation(c => c.Add<Child>(422, p => p.Title(\"Child\"))); return Builder.Services; } }")]
    [InlineData("public Setup() { Builder.Services.AddFlowOnRailsTranslation(c => c.Add<Child>(422, p => p.Title(\"Child\"))); }")]
    public async Task DeferredMembers_DoNotInstallTheirCatalogs(string member)
    {
        var diagnostics = await EndpointMappingAnalyzerTests.Analyze("""
            Setup.Builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = Setup.Builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            """, $$"""
            public class Setup {
                public static WebApplicationBuilder Builder = WebApplication.CreateBuilder();
                {{member}}
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FORHTTP009");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FORHTTP010");
    }

    [Theory]
    [InlineData("dynamic", "object")]
    [InlineData("(int First, int Second)", "(int Left, int Right)")]
    public async Task ExactMappings_UseRuntimeTypeIdentity(string returnedType, string mappedType)
    {
        var diagnostics = await EndpointMappingAnalyzerTests.Analyze($$"""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.ForTree<Error>().TranslateEachType());
            builder.Services.AddFlowOnRailsTranslation(c => c.Add<ErrorFor<{{mappedType}}>>(422, p => p.Title("Failure")));
            var app = builder.Build();
            app.MapGet("/", () => GenericHandlers.Read().ToHttpResult(() => Results.Ok()));
            """, $$"""
            public sealed record ErrorFor<T> : Error;
            public static class GenericHandlers {
                public static Rail Read() => new ErrorFor<{{returnedType}}>();
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FORHTTP009");
    }

    [Fact]
    public async Task ExactVisibility_UsesRuntimeTypeIdentity()
    {
        var diagnostics = await EndpointMappingAnalyzerTests.Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<ErrorFor<object>>().TranslateEachType());
            var app = builder.Build();
            app.MapGet("/", () => GenericHandlers.Read().ToHttpResult(() => Results.Ok()));
            """, """
            public sealed record ErrorFor<T> : Error;
            public static class GenericHandlers {
                public static Rail Read() => new ErrorFor<dynamic>();
            }
            """);

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORHTTP009");
    }

    [Fact]
    public async Task InterfaceContract_ChecksKnownExactErrorDespiteOpenDispatch()
    {
        var diagnostics = await EndpointMappingAnalyzerTests.Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = builder.Build();
            IReader reader = null!;
            app.MapGet("/", () => reader.Read().ToHttpResult(() => Results.Ok()));
            """, """
            public interface IReader {
                [ResultsWith<Child>] Rail Read();
            }
            """);

        Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORHTTP009");
    }

    [Fact]
    public async Task InterfaceContract_DoesNotDemandExactMapForOpenFamilyRoot()
    {
        var diagnostics = await EndpointMappingAnalyzerTests.Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddFlowOnRailsVisibility(p => p.Global.ForTree<Parent>().TranslateEachType());
            var app = builder.Build();
            IReader reader = null!;
            app.MapGet("/", () => reader.Read().ToHttpResult(() => Results.Ok()));
            """, """
            public interface IReader {
                [ResultsWith<Parent>] Rail Read();
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FORHTTP009");
    }

    [Fact]
    public async Task InterfaceContract_KnownExactErrorHonorsIntentionalControllerGeneric()
    {
        var diagnostics = await EndpointMappingAnalyzerTests.Analyze("""
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers();
            builder.Services.AddFlowOnRailsVisibility(p => {
                p.Global.ForTree<Parent>().TranslateEachType();
                p.ForController<UsersController>(s => s.For<Child>().UseGenericProblem());
            });
            var app = builder.Build();
            app.MapControllers();
            """, """
            public interface IReader {
                [ResultsWith<Child>] Rail Read();
            }
            public class UsersController(IReader reader) : ControllerBase {
                public IActionResult Get() => reader.Read().ToActionResult(() => Ok());
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FORHTTP009");
    }
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ControllerUsesApplicationOfItsRouteRegistration(bool configureUnusedBuilder, bool missing)
    {
        var configuration = "builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());";
        var unusedConfiguration = configureUnusedBuilder ? configuration.Replace("builder.", "unused.") : "";
        var actualConfiguration = configureUnusedBuilder ? "" : configuration;
        var diagnostics = await EndpointMappingAnalyzerTests.Analyze($$"""
            var unused = WebApplication.CreateBuilder();
            {{unusedConfiguration}}
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllers();
            {{actualConfiguration}}
            var app = builder.Build();
            app.MapControllers();
            """, """
            public class ReadController : ControllerBase {
                public IActionResult Get() => Handlers.Read().ToActionResult(() => Ok());
            }
            """);

        Assert.Equal(missing ? 1 : 0, diagnostics.Count(diagnostic => diagnostic.Id == "FORHTTP009"));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FORHTTP010");
    }
    [Fact]
    public async Task BuilderProperty_DoesNotEstablishSharedApplicationIdentity()
    {
        var diagnostics = await EndpointMappingAnalyzerTests.Analyze("""
            Setup.Builder.Services.AddFlowOnRailsVisibility(p => p.Global.For<Child>().TranslateEachType());
            var app = Setup.Builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            """, """
            public static class Setup {
                public static WebApplicationBuilder Builder => WebApplication.CreateBuilder();
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FORHTTP009");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "FORHTTP010");
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaticRegistrationSyntax_UsesServiceCollectionArgument(bool registerMapping)
    {
        var mapping = registerMapping
            ? "TranslationServiceExtensions.AddFlowOnRailsTranslation(builder.Services, c => c.Add<Child>(422, p => p.Title(\"Child\")));"
            : "";
        var diagnostics = await EndpointMappingAnalyzerTests.Analyze($$"""
            var builder = WebApplication.CreateBuilder();
            Microsoft.Extensions.DependencyInjection.ErrorPolicyServiceExtensions.AddFlowOnRailsVisibility(
                builder.Services, p => p.Global.For<Child>().TranslateEachType());
            {{mapping}}
            var app = builder.Build();
            app.MapGet("/", () => Handlers.Read().ToHttpResult(() => Results.Ok()));
            """);

        Assert.Equal(registerMapping ? 0 : 1, diagnostics.Count(diagnostic => diagnostic.Id == "FORHTTP009"));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "FORHTTP010");
    }
}
