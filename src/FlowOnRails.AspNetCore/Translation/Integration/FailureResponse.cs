using System.Text.Json;

using FlowOnRails.AspNetCore.Visibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FlowOnRails.AspNetCore.Translation;

internal static class FailureResponse
{
    internal static ProblemDetails Create(Error error, HttpContext context)
    {
        if (context.Response.HasStarted)
            throw new InvalidOperationException("An error response cannot replace a response that has started.");

        var translator = context.RequestServices.GetRequiredService<ProblemTranslator>();
        var policy = context.RequestServices.GetRequiredService<ErrorPolicy>();

        return translator.Translate(error, context, policy);
    }

    internal static async Task WriteHttpAsync(Error error, HttpContext context)
    {
        var problem = Create(error, context);
        context.Response.StatusCode = problem.Status!.Value;
        var writer = context.RequestServices.GetService<IProblemDetailsService>();

        if (writer is not null && await writer.TryWriteAsync(new()
            {
                HttpContext = context,
                ProblemDetails = problem
            }).ConfigureAwait(false))
            return;

        if (context.Response.HasStarted)
            throw new InvalidOperationException("The problem writer started a response without completing it.");

        context.Response.StatusCode = problem.Status!.Value;
        await context.Response.WriteAsJsonAsync(problem,
            options: (JsonSerializerOptions?)null,
            contentType: "application/problem+json",
            cancellationToken: context.RequestAborted).ConfigureAwait(false);
    }

    internal static Task WriteMvcAsync(Error error, ActionContext actionContext)
    {
        var context = actionContext.HttpContext;
        var problem = Create(error, context);
        var options = context.RequestServices.GetService<IOptions<ProblemDetailsOptions>>();
        options?.Value.CustomizeProblemDetails?.Invoke(new()
        {
            HttpContext = context,
            ProblemDetails = problem
        });
        var result = new ObjectResult(problem)
        {
            StatusCode = problem.Status
        };
        result.ContentTypes.Add("application/problem+json");
        result.ContentTypes.Add("application/problem+xml");

        return result.ExecuteResultAsync(actionContext);
    }
}
