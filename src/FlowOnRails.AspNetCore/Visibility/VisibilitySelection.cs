namespace FlowOnRails.AspNetCore.Visibility;

/// <summary>Assigns a behavior to a selected error type or family.</summary>
public sealed class VisibilitySelection
{
    private readonly VisibilityRule _rule;

    internal VisibilitySelection(VisibilityRule rule) => _rule = rule;

    /// <summary>Permit each selected type's own translation contract.</summary>
    public void TranslateEachType() => _rule.SetBehavior(VisibilityBehavior.TranslateEachType);

    /// <summary>Use the generic response for the selected errors.</summary>
    public void UseGenericProblem() => _rule.SetBehavior(VisibilityBehavior.GenericProblem);

    /// <summary>Search outside the selection for an authorized ancestor translation contract.</summary>
    public void
        UseNearestAncestorMapping(
        ) => _rule.SetBehavior(VisibilityBehavior.NearestAncestorMapping);
}
