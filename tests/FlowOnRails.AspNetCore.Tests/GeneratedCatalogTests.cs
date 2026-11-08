using System.Net.Http.Json;
using System.Text.Json;

using FlowOnRails.AspNetCore.Translation;
using FlowOnRails.TestDomain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowOnRails.AspNetCore.Tests;

public class GeneratedCatalogTests
{
    [Fact]
    public async Task GeneratedCatalog_TranslatesRealRequestsWithAutomaticManualAndGenericMaps()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddFlowOnRailsVisibility(policy => policy.Global.ForTree<Error>().TranslateEachType());
        builder.Services.AddGeneratedProblems();
        await using var app = builder.Build();
        var id = Guid.NewGuid();
        app.MapGet("/automatic", () => Rail.Fail(new UserNotFound(id)).ToHttpResult(() => Results.Ok()));
        app.MapGet("/manual", () => Rail.Fail(new ValidationError<int>(42)).ToHttpResult(() => Results.Ok()));
        app.MapGet("/generic", () => Rail.Fail(new UserNotFound(id)).ToHttpResult(() => Results.Ok()))
            .WithErrorVisibility(scope => scope.ForTree<Error>().UseGenericProblem());
        app.MapGet("/ancestor", () => Rail.Fail(new UserNotFound(id)).ToHttpResult(() => Results.Ok()))
            .WithErrorVisibility(scope =>
            {
                scope.For<UserNotFound>().UseNearestAncestorMapping();
                scope.For<DomainNotFound>().TranslateEachType();
            });
        app.MapGet("/hiding", () => Rail.Fail(new HiddenCodeError()).ToHttpResult(() => Results.Ok()));
        app.MapGet("/collision", () => Rail.Fail(new ReservedError()).ToHttpResult(() => Results.Ok()));
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var automatic = await ReadProblem(client, "/automatic");
        Assert.Equal(404, automatic.GetProperty("status").GetInt32());
        var data = automatic.GetProperty("data");
        Assert.Equal(id, data.GetProperty("Id").GetGuid());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("Reason").ValueKind);
        Assert.Equal("user", data.GetProperty("Resource").GetString());
        var manual = await ReadProblem(client, "/manual");
        Assert.Equal(42, manual.GetProperty("invalid").GetInt32());
        Assert.False(manual.TryGetProperty("InvalidValue", out _));
        var generic = await ReadProblem(client, "/generic");
        Assert.Equal(503, generic.GetProperty("status").GetInt32());
        Assert.Equal("Unavailable", generic.GetProperty("title").GetString());
        Assert.True(generic.TryGetProperty("request", out _));
        Assert.False(generic.TryGetProperty("data", out _));
        var ancestor = await ReadProblem(client, "/ancestor");
        Assert.Equal("user", ancestor.GetProperty("Resource").GetString());
        Assert.False(ancestor.TryGetProperty("Id", out _));
        var hidden = await ReadProblem(client, "/hiding");
        Assert.Equal("derived", hidden.GetProperty("Code").GetString());
        var collision = await ReadProblem(client, "/collision");
        Assert.Equal(409, collision.GetProperty("status").GetInt32());
        Assert.Equal("preserved", collision.GetProperty("Status").GetString());
    }

    private static async Task<JsonElement> ReadProblem(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(problem.GetProperty("status").GetInt32(), (int)response.StatusCode);

        return problem;
    }

    [Fact]
    public void GeneratedRegistration_RejectsDuplicatesAndRunsConfigurationOnce()
    {
        var callsBefore = GeneratedProblems.ConfigurationCalls;
        var services = new ServiceCollection();
        services.AddGeneratedProblems();

        Assert.Equal(callsBefore + 1, GeneratedProblems.ConfigurationCalls);
        Assert.Throws<InvalidOperationException>(() => services.AddGeneratedProblems());
        Assert.Equal(callsBefore + 1, GeneratedProblems.ConfigurationCalls);
    }
}

#pragma warning disable FORHTTP007
public sealed record ReservedError : Error
{
    public string status => throw new Exception("A reserved getter must not execute");
    public string Status => "preserved";
}
#pragma warning restore FORHTTP007

[ErrorAssembly(typeof(DomainError))]
[ErrorAssembly(typeof(UserNotFound))]
public static partial class GeneratedProblems
{
    public static int ConfigurationCalls { get; private set; }

    [HttpProblem(404), AutomaticExtensions, UseDataField]
    private static void ArbitraryMethodName(ProblemBuilder<UserNotFound> problem)
    {
        ConfigurationCalls++;
        problem.Type("https://example.test/user-not-found");
        problem.Title("Missing user");
    }

    [HttpProblem(404), AutomaticExtensions]
    private static void Ancestor(ProblemBuilder<DomainNotFound> problem) => problem.Title("Missing resource");

    [HttpProblem(422)]
    private static void Manual(ProblemBuilder<ValidationError<int>> problem) =>
        problem.Extensions((error, data) => data["invalid"] = error.InvalidValue);

    [HttpProblem(400), AutomaticExtensions]
    private static void Hiding(ProblemBuilder<HiddenCodeError> problem) { }

    // This test deliberately exercises the recoverable warning with warnings-as-errors enabled.
#pragma warning disable FORHTTP007
    [HttpProblem(409), AutomaticExtensions]
    private static void Collision(ProblemBuilder<ReservedError> problem) { }
#pragma warning restore FORHTTP007

    [GenericProblem(503)]
    private static void Generic(GenericProblemBuilder problem)
    {
        problem.Type("https://example.test/unavailable");
        problem.Title("Unavailable");
        problem.Extensions((context, data) => data["request"] = context.TraceIdentifier);
    }
}
