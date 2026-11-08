namespace FlowOnRails.AspNetCore.Visibility;

/// <summary>Assigns a behavior to a selected error type or family.</summary>
public sealed class RuleSelection
{
    private readonly Rule _rule;

    internal RuleSelection(Rule rule) => _rule = rule;

    /// <summary>Permit each selected type's own translation contract.</summary>
    public void TranslateEachType() =>
        _rule.SetBehavior(RuleBehavior.TranslateEachType);

    /// <summary>Use the generic response for the selected errors.</summary>
    public void UseGenericProblem() =>
        _rule.SetBehavior(RuleBehavior.GenericProblem);

    /// <summary>Search outside the selection for an authorized ancestor translation contract.</summary>
    public void UseNearestAncestorMapping() =>
        _rule.SetBehavior(RuleBehavior.NearestAncestorMapping);
}
