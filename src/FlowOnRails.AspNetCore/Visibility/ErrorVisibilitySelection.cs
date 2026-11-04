namespace FlowOnRails.AspNetCore.Visibility;

/// <summary>Assigns a behavior to a selected error type or family.</summary>
public sealed class ErrorVisibilitySelection
{
    private readonly VisibilityRule _rule;

    internal ErrorVisibilitySelection(VisibilityRule rule) => _rule = rule;

    /// <summary>Permit each selected type's own translation contract.</summary>
    public void TranslateEachType() => _rule.SetBehavior(ErrorVisibilityBehavior.TranslateEachType);

    /// <summary>Use the generic response for the selected errors.</summary>
    public void UseGenericProblem() => _rule.SetBehavior(ErrorVisibilityBehavior.GenericProblem);

    /// <summary>Search outside the selection for an authorized ancestor translation contract.</summary>
    public void UseNearestAncestorMapping() => _rule.SetBehavior(ErrorVisibilityBehavior.NearestAncestorMapping);
}
