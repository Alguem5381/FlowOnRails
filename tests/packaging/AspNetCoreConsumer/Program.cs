using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FlowOnRails;
using FlowOnRails.AspNetCore.Translation;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddControllers().AddXmlSerializerFormatters();
builder.Services.AddProblemDetails();
builder.Services.AddFlowOnRailsVisibility(policy =>
    policy.Global.ForTree<Error>().TranslateEachType());
builder.Services.AddPackageProblems();

await using var app = builder.Build();
app.MapControllers();
app.MapGet("/ok", () => Rail.Ok(new Payload { Id = 42 }).ToHttpResult(Results.Ok));
app.MapGet("/automatic", () => Rail.Fail<Payload>(new Missing(42, null)).ToHttpResult(Results.Ok));
app.MapGet("/manual", () => Rail.Fail(new Invalid(42)).ToHttpResult(() => Results.Ok()));
app.MapGet("/hidden", () => Rail.Fail(new Missing(42, null)).ToHttpResult(() => Results.Ok()))
    .WithErrorVisibility(scope => scope.ForTree<Error>().UseGenericProblem());
app.MapGet("/empty", () => Rail.Ok().ToHttpResult(Results.NoContent));

try
{
    await app.StartAsync();
    using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
    using var ok = await client.GetAsync("/ok");
    var payload = await ok.Content.ReadFromJsonAsync<JsonElement>();
    Require(ok.StatusCode == HttpStatusCode.OK && payload.GetProperty("id").GetInt32() == 42,
        "Minimal API success did not preserve the payload.");

    var automatic = await ReadProblem(client, "/automatic", HttpStatusCode.NotFound);
    var data = automatic.GetProperty("data");
    Require(data.GetProperty("Id").GetInt32() == 42 &&
            data.GetProperty("Reason").ValueKind == JsonValueKind.Null,
        "The packaged generator did not preserve automatic properties and null values.");

    var manual = await ReadProblem(client, "/manual", HttpStatusCode.UnprocessableEntity);
    Require(manual.GetProperty("invalidId").GetInt32() == 42,
        "Manual extensions were not executed.");

    var generic = await ReadProblem(client, "/hidden", HttpStatusCode.InternalServerError);
    Require(generic.TryGetProperty("requestId", out _) && !generic.TryGetProperty("data", out _),
        "The hidden error did not use the request-aware generic problem.");

    using var empty = await client.GetAsync("/empty");
    Require(empty.StatusCode == HttpStatusCode.NoContent &&
            (await empty.Content.ReadAsByteArrayAsync()).Length == 0,
        "The no-content response has an unexpected status or body.");

    var mvc = await ReadProblem(client, "/mvc/error", HttpStatusCode.NotFound);
    Require(mvc.GetProperty("data").GetProperty("Id").GetInt32() == 42,
        "The MVC failure did not execute the generated map.");

    using var request = new HttpRequestMessage(HttpMethod.Get, "/mvc/ok");
    request.Headers.Accept.ParseAdd("application/xml");
    using var xml = await client.SendAsync(request);
    Require(xml.StatusCode == HttpStatusCode.OK &&
            xml.Content.Headers.ContentType?.MediaType == "application/xml" &&
            (await xml.Content.ReadAsStringAsync()).Contains("<Id>42</Id>", StringComparison.Ordinal),
        "The MVC success did not preserve XML formatting.");

    Console.WriteLine("ASP.NET Core package: seven HTTP/MVC checks passed.");
}
finally
{
    await app.StopAsync();
}

static async Task<JsonElement>
    ReadProblem(
        HttpClient client,
        string path,
        HttpStatusCode status
    )
{
    using var response = await client.GetAsync(path);
    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    Require(response.StatusCode == status && problem.GetProperty("status").GetInt32() == (int)status,
        $"Unexpected problem status at {path}.");

    return problem;
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

public sealed class Payload
{
    public int Id { get; set; }
}

public sealed record Missing(int Id, string? Reason) : Error;
public sealed record Invalid(int Id) : Error;

public static partial class PackageProblems
{
#if INVALID_STATUS
    [HttpProblem(399)]
#else
    [HttpProblem(404)]
#endif
    [AutomaticExtensions, UseDataField]
    private static void Automatic(ProblemBuilder<Missing> problem) => problem.Title("Missing");

    [HttpProblem(422)]
    private static void Manual(ProblemBuilder<Invalid> problem) =>
        problem.Extensions((error, data) => data["invalidId"] = error.Id);

    [GenericProblem]
    private static void Generic(GenericProblemBuilder problem) =>
        problem.Extensions((context, data) => data["requestId"] = context.TraceIdentifier);
}

[ApiController, Route("mvc")]
public sealed class PackageController : ControllerBase
{
    [HttpGet("ok")]
    public ActionResult<Payload> OkResult() =>
        Rail.Ok(new Payload { Id = 42 }).ToActionResult(Ok);

    [HttpGet("error")]
    public ActionResult<Payload> ErrorResult() =>
        Rail.Fail<Payload>(new Missing(42, null)).ToActionResult(Ok);
}
