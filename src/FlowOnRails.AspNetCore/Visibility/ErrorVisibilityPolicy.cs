using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace FlowOnRails.AspNetCore.Visibility;

/// <summary>Resolves immutable visibility rules without reading error properties.</summary>
public sealed class ErrorVisibilityPolicy
{
    private readonly VisibilityRule[] _rules;

    internal ErrorVisibilityPolicy(VisibilityRule[] rules) => _rules = rules;

    /// <summary>
    /// Resolve a closed error type. Exact controller rules precede global rules; within a scope,
    /// the nearest error root wins, with exact, descendants-only, then tree rules breaking ties.
    /// </summary>
    public ErrorVisibilityResolution Resolve(Type errorType, Type? controllerType = null)
    {
        ArgumentNullException.ThrowIfNull(errorType);

        if (!typeof(Error).IsAssignableFrom(errorType) || errorType.ContainsGenericParameters)
            throw new ArgumentException("A closed type derived from Error is required.", nameof(errorType));

        if (controllerType is not null)
            ValidateController(controllerType);

        var effectiveRule = FindRule(errorType, controllerType);
        var behavior = effectiveRule?.Behavior ?? ErrorVisibilityBehavior.GenericProblem;

        switch (behavior)
        {
            case ErrorVisibilityBehavior.GenericProblem:
                return new(behavior, []);
            case ErrorVisibilityBehavior.TranslateEachType:
                return new(behavior, [errorType]);
        }

        var candidates = new List<Type>();

        for (
            var ancestor = errorType.BaseType;
            ancestor is not null && typeof(Error).IsAssignableFrom(ancestor);
            ancestor = ancestor.BaseType
        )
        {
            var rule = FindRule(ancestor, controllerType);
            // An explicit generic response is a boundary; an unconfigured ancestor is not.
            if (rule?.Behavior == ErrorVisibilityBehavior.GenericProblem)
                break;

            if (effectiveRule is not null && effectiveRule.Selection.Matches(ancestor))
                continue;

            if (rule?.Behavior == ErrorVisibilityBehavior.TranslateEachType)
                candidates.Add(ancestor);
        }

        return new ErrorVisibilityResolution(behavior, candidates);
    }

    /// <summary>
    /// Resolve an error using the current endpoint's controller metadata.
    /// Endpoints without controller metadata use global rules. No controller instance is created.
    /// </summary>
    public ErrorVisibilityResolution ResolveForRequest(Error error, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(httpContext);

        var controllerType = httpContext.GetEndpoint()?.Metadata
            .GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType();

        return Resolve(error.GetType(), controllerType);
    }

    private VisibilityRule? FindRule(Type errorType, Type? controllerType)
    {
        if (controllerType is null)
            return FindInScope(errorType, null);

        var local = FindInScope(errorType, controllerType);

        return local ?? FindInScope(errorType, null);
    }

    private VisibilityRule? FindInScope(Type errorType, Type? controllerType)
    {
        for (var ancestor = errorType;
             ancestor is not null && typeof(Error).IsAssignableFrom(ancestor);
             ancestor = ancestor.BaseType)
        {
            var match = _rules
                .Where(rule => rule.ControllerType == controllerType &&
                               rule.Selection.Root == ancestor &&
                               rule.Selection.Matches(errorType))
                .OrderBy(rule => rule.Selection.Range)
                .FirstOrDefault();

            if (match is not null)
                return match;
        }

        return null;
    }

    internal static void ValidateController(Type controllerType)
    {
        if (!controllerType.IsClass || controllerType.ContainsGenericParameters)
            throw new ArgumentException("A closed controller class is required.", nameof(controllerType));
    }
}
