using System.Collections.Frozen;

using FlowOnRails.AspNetCore.Visibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FlowOnRails.AspNetCore.Translation;

/// <summary>Projects only the first mapped contract authorized for the current request.</summary>
public sealed class ProblemTranslator
{
    private readonly FrozenDictionary<Type, Func<Error, HttpContext, ProblemDetails>?> _mappings;
    private readonly Func<HttpContext, ProblemDetails> _generic;

    internal ProblemTranslator(
        FrozenDictionary<Type, Func<Error, HttpContext, ProblemDetails>?> mappings,
        Func<HttpContext, ProblemDetails> generic)
    {
        _mappings = mappings;
        _generic = generic;
    }

    /// <summary>Creates a fresh problem using visibility and services belonging to this request.</summary>
    public ProblemDetails Translate(Error error, HttpContext context, ErrorPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(policy);

        context.RequestAborted.ThrowIfCancellationRequested();

        foreach (var candidate in policy.ResolveForRequest(error, context).TranslationCandidates)
            if (_mappings.TryGetValue(candidate, out var mapping) && mapping is not null)
                return mapping(error, context);

        return _generic(context);
    }
}
