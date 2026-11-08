using System.Collections.Frozen;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace FlowOnRails.AspNetCore.Visibility;

/// <summary>Resolves immutable visibility rules without reading error properties.</summary>
public sealed class ErrorPolicy
{
    private readonly RuleIndex _global;
    private readonly FrozenDictionary<Type, RuleIndex> _controllers;

    internal ErrorPolicy(Rule[] rules)
    {
        _global = new(rules.Where(rule => rule.ControllerType is null));
        _controllers = rules
            .Where(rule => rule.ControllerType is not null)
            .GroupBy(rule => rule.ControllerType!)
            .ToFrozenDictionary(group => group.Key, group => new RuleIndex(group));
    }

    /// <summary>
    /// Resolve a closed error type. Exact controller rules precede global rules; within a scope,
    /// the nearest error root wins, with exact, descendants-only, then tree rules breaking ties.
    /// </summary>
    public ExposureDecision
        Resolve(
            Type errorType,
            Type? controllerType = null
        ) =>
        Resolve(errorType, controllerType, []);

    internal RuleIndex GlobalIndex => _global;

    private ExposureDecision
        Resolve(
            Type errorType,
            Type? controllerType,
            IReadOnlyList<EndpointScopeMetadata> endpointScopes
        )
    {
        ArgumentNullException.ThrowIfNull(errorType);

        if (!typeof(Error).IsAssignableFrom(errorType) || errorType.ContainsGenericParameters)
            throw new ArgumentException("A closed type derived from Error is required.", nameof(errorType));

        if (controllerType is not null)
            ValidateController(controllerType);

        var effectiveRule = FindRule(errorType, controllerType, endpointScopes);
        var behavior = effectiveRule?.Behavior ?? RuleBehavior.GenericProblem;

        switch (behavior)
        {
            case RuleBehavior.GenericProblem:
                return new(behavior, []);
            case RuleBehavior.TranslateEachType:
                return new(behavior, [errorType]);
        }

        var candidates = new List<Type>();

        for (
            var ancestor = errorType.BaseType;
            ancestor is not null && typeof(Error).IsAssignableFrom(ancestor);
            ancestor = ancestor.BaseType
        )
        {
            var rule = FindRule(ancestor, controllerType, endpointScopes);
            // An explicit generic response is a boundary; an unconfigured ancestor is not.
            if (rule?.Behavior == RuleBehavior.GenericProblem)
                break;

            if (effectiveRule is not null && effectiveRule.Selection.IncludesType(ancestor))
                continue;

            if (rule?.Behavior == RuleBehavior.TranslateEachType)
                candidates.Add(ancestor);
        }

        return new ExposureDecision(behavior, candidates);
    }

    /// <summary>
    /// Resolve an error using the current endpoint's controller metadata.
    /// Endpoint and route-group rules precede controller and global rules. No controller instance is created.
    /// </summary>
    public ExposureDecision
        ResolveForRequest(
            Error error,
            HttpContext httpContext
        )
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(httpContext);

        var endpoint = httpContext.GetEndpoint();
        var controllerType = endpoint?.Metadata
            .GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType();
        var endpointScopes = endpoint?.Metadata.GetOrderedMetadata<EndpointScopeMetadata>() ?? [];

        return Resolve(error.GetType(), controllerType, endpointScopes);
    }

    private Rule?
        FindRule(
            Type errorType,
            Type? controllerType,
            IReadOnlyList<EndpointScopeMetadata> endpointScopes
        )
    {
        // Endpoint metadata is ordered outer group, inner groups, then endpoint.
        for (
            var index = endpointScopes.Count - 1;
            index >= 0;
            index--
        )
        {
            var match = endpointScopes[index].Index.FindRule(errorType);

            if (match is not null)
                return match;
        }

        if (controllerType is not null &&
            _controllers.TryGetValue(controllerType, out var controller))
        {
            var local = controller.FindRule(errorType);

            if (local is not null)
                return local;
        }

        return _global.FindRule(errorType);
    }

    internal static void ValidateController(Type controllerType)
    {
        if (!controllerType.IsClass || controllerType.ContainsGenericParameters)
            throw new ArgumentException("A closed controller class is required.", nameof(controllerType));
    }
}
