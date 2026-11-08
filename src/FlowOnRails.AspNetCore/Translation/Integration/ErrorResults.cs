using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FlowOnRails.AspNetCore.Translation;

internal sealed class ErrorHttpResult(Error error) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext) =>
        FailureResponse.WriteHttpAsync(error, httpContext);
}

internal sealed class ErrorActionResult(Error error) : ActionResult
{
    public override Task ExecuteResultAsync(ActionContext context) =>
        FailureResponse.WriteMvcAsync(error, context);
}

internal sealed class NativeActionResult(IActionResult result) : ActionResult
{
    public override Task ExecuteResultAsync(ActionContext context) =>
        result.ExecuteResultAsync(context);
}
