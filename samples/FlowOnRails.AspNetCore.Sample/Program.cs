using FlowOnRails;
using FlowOnRails.AspNetCore.Sample;
using FlowOnRails.AspNetCore.Translation;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddXmlSerializerFormatters();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<UserService>();
builder.Services.AddFlowOnRailsVisibility(policy =>
    policy.Global.ForTree<SampleError>().TranslateEachType());
builder.Services.AddApiProblems();

var app = builder.Build();
app.MapControllers();
app.MapGet("/minimal/users/{id:int}", (int id, UserService service) =>
    service.Find(id).ToHttpResult(user => Results.Ok(user)));
app.MapGet("/minimal/hidden/{id:int}", (int id, UserService service) =>
    service.Find(id).ToHttpResult(user => Results.Ok(user)))
    .WithErrorVisibility(scope => scope.ForTree<SampleError>().UseGenericProblem());
app.MapGet("/minimal/empty", () => Rail.Ok().ToHttpResult(() => Results.NoContent()));
app.Run();
