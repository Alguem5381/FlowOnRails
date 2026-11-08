using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using FlowOnRails.AspNetCore.Translation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowOnRails.AspNetCore.Tests;

public class ResultIntegrationTests
{
    [Fact]
    public void SuccessFactories_RunOnceAndPreserveNativeResults()
    {
        var calls = 0;
        var native = Results.Text("hello");
        var http = Rail.Ok(42).ToHttpResult(value => { calls += value; return native; });
        Assert.Same(native, http);
        Assert.Equal(42, calls);
        var mvc = new OkObjectResult("hello");
        Assert.Same(mvc, Rail.Ok().ToActionResult(() => mvc));
        ActionResult<Payload> typed = Rail.Ok(new Payload()).ToActionResult(value => new OkObjectResult(value));
        Assert.IsType<OkObjectResult>(typed.Result);
    }

    [Fact]
    public void FailedConversions_DoNotExecuteSuccessFactories()
    {
        var failure = new EndpointError("missing");
        _ = Rail.Fail(failure).ToHttpResult(() => throw new Exception("not called"));
        _ = Rail.Fail<Payload>(failure).ToHttpResult(_ => throw new Exception("not called"));
        _ = Rail.Fail(failure).ToActionResult(() => throw new Exception("not called"));
        _ = Rail.Fail<Payload>(failure).ToActionResult(_ => throw new Exception("not called"));
    }

    [Fact]
    public async Task ArbitraryIActionResult_IsExecutedWithItsOriginalActionContext()
    {
        var native = new ContextResult();
        var context = new ActionContext { HttpContext = new DefaultHttpContext() };
        var result = Rail.Ok().ToActionResult(() => native);

        await result.ExecuteResultAsync(context);

        Assert.Same(context, native.Context);
    }

    [Fact]
    public void InvalidRailsCallbacksAndNullResults_RemainProgrammingErrors()
    {
        Assert.Throws<InvalidOperationException>(() => default(Rail).ToHttpResult(() => Results.Ok()));
        Assert.Throws<InvalidOperationException>(() => default(RailFor<int>).ToActionResult(value => new OkObjectResult(value)));
        Assert.Throws<ArgumentNullException>(() => Rail.Ok().ToHttpResult(null!));
        Assert.Throws<ArgumentNullException>(() => Rail.Ok(1).ToActionResult(null!));
        Assert.Throws<InvalidOperationException>(() => Rail.Ok().ToHttpResult(() => null!));
        Assert.Throws<InvalidOperationException>(() => Rail.Ok(1).ToActionResult(_ => null!));
        var expected = new Exception("callback");
        Assert.Same(expected, Assert.Throws<Exception>(() => Rail.Ok().ToHttpResult(() => throw expected)));
    }

    [Fact]
    public async Task HttpAndMvc_RespectTheirOwnJsonOptionsAndTranslateFailures()
    {
        await using var app = await Start();
        using var client = Client(app);
        var http = await client.GetFromJsonAsync<JsonElement>("/success");
        var mvc = await client.GetFromJsonAsync<JsonElement>("/native/success");
        Assert.Equal("Ada", http.GetProperty("display_name").GetString());
        Assert.Equal("Ada", mvc.GetProperty("DisplayName").GetString());

        foreach (var path in new[] { "/failure", "/native/failure" })
        {
            using var response = await client.GetAsync(path);
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal(404, json.GetProperty("status").GetInt32());
            Assert.Equal("missing", json.GetProperty("Resource").GetString());
            Assert.True(json.GetProperty("customized").GetBoolean());
        }
    }

    [Fact]
    public async Task MvcUsesXmlFormatter_WhileHttpResultUsesJson()
    {
        await using var app = await Start();
        using var client = Client(app);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/xml");
        using var mvc = await client.GetAsync("/native/success");
        using var http = await client.GetAsync("/success");
        using var failure = await client.GetAsync("/failure");

        Assert.Equal("application/xml", mvc.Content.Headers.ContentType!.MediaType);
        Assert.Contains("<DisplayName>Ada</DisplayName>", await mvc.Content.ReadAsStringAsync());
        Assert.Equal("application/json", http.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, failure.StatusCode);
        Assert.Equal("application/problem+json", failure.Content.Headers.ContentType!.MediaType);
        var problem = await failure.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("customized").GetBoolean());
    }

    [Fact]
    public async Task NativeCreatedAndFileResults_WorkWithoutDedicatedRailHelpers()
    {
        await using var app = await Start();
        using var client = Client(app);
        using var created = await client.PostAsync("/create", null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.EndsWith("/success", created.Headers.Location!.ToString());
        using var file = await client.GetAsync("/native/file");
        Assert.Equal("file contents", await file.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", file.Content.Headers.ContentType!.MediaType);
        using var empty = await client.GetAsync("/empty");
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);
        Assert.Empty(await empty.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task EndpointMetadataAndApplications_RemainIsolated()
    {
        await using var visible = await Start();
        await using var hidden = await Start(hide: true);
        using var first = Client(visible);
        using var second = Client(hidden);
        using var visibleResponse = await first.GetAsync("/failure");
        using var hiddenResponse = await second.GetAsync("/failure");
        using var localResponse = await first.GetAsync("/hidden");

        Assert.Equal(HttpStatusCode.NotFound, visibleResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, hiddenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, localResponse.StatusCode);
        var generic = await hiddenResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Try later", generic.GetProperty("title").GetString());
        Assert.False(generic.TryGetProperty("Resource", out _));
    }

    private static async Task<WebApplication> Start(bool hide = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(TranslationController).Assembly)
            .AddJsonOptions(options => options.JsonSerializerOptions.PropertyNamingPolicy = null)
            .AddXmlSerializerFormatters();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["customized"] = true);
        builder.Services.AddFlowOnRailsVisibility(policy =>
        {
            if (hide)
                policy.Global.ForTree<Error>().UseGenericProblem();
            else
                policy.Global.ForTree<Error>().TranslateEachType();
        });
        builder.Services.AddFlowOnRailsTranslation(catalog =>
        {
            catalog.Add<EndpointError>(404, problem => problem.Extensions((error, data) => data["Resource"] = error.Resource));
            catalog.Generic(503, problem =>
            {
                problem.Type("https://example.test/unavailable");
                problem.Title("Try later");
                problem.Extensions((context, data) => data["request"] = context.TraceIdentifier);
            });
        });
        var app = builder.Build();
        app.MapControllers();
        app.MapGet("/success", () => Rail.Ok(new Payload { DisplayName = "Ada" }).ToHttpResult(value => Results.Ok(value)))
            .WithName("success");
        app.MapGet("/failure", () => Rail.Fail<Payload>(new EndpointError("missing"))
            .ToHttpResult(_ => throw new Exception("must not run")));
        app.MapGet("/hidden", () => Rail.Fail(new EndpointError("hidden")).ToHttpResult(() => Results.Ok()))
            .WithErrorVisibility(scope => scope.ForTree<Error>().UseGenericProblem());
        app.MapPost("/create", () => Rail.Ok(new Payload()).ToHttpResult(value =>
            Results.CreatedAtRoute("success", value: value)));
        app.MapGet("/empty", () => Rail.Ok().ToHttpResult(() => Results.NoContent()));

        await app.StartAsync();

        return app;
    }

    private static HttpClient Client(WebApplication app) => new() { BaseAddress = new Uri(app.Urls.Single()) };

    private sealed class ContextResult : IActionResult
    {
        internal ActionContext? Context { get; private set; }
        public Task ExecuteResultAsync(ActionContext context)
        {
            Context = context;

            return Task.CompletedTask;
        }
    }
}

public sealed record EndpointError(string Resource) : Error;

public class Payload
{
    public string DisplayName { get; set; } = "";
}

[ApiController]
[Route("native")]
public sealed class TranslationController : ControllerBase
{
    [HttpGet("success")]
    public ActionResult<Payload> Success() =>
        Rail.Ok(new Payload { DisplayName = "Ada" }).ToActionResult(value => Ok(value));

    [HttpGet("failure")]
    public ActionResult<Payload> Failure() =>
        Rail.Fail<Payload>(new EndpointError("missing")).ToActionResult(_ => throw new Exception("must not run"));

    [HttpGet("file")]
    public ActionResult FileResponse() =>
        Rail.Ok("file contents").ToActionResult(value => File(Encoding.UTF8.GetBytes(value), "text/plain", "example.txt"));
}
