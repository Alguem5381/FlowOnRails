namespace FlowOnRails.AspNetCore.Visibility;

/// <summary>A bounded error family with optional behavior and narrower selections.</summary>
public sealed class ErrorVisibilityTree : ErrorVisibilityRules
{
    private readonly List<VisibilityRule> _rules;
    private readonly Type? _controllerType;
    private readonly ErrorSelection _selection;

    internal ErrorVisibilityTree(List<VisibilityRule> rules, Type? controllerType, ErrorSelection selection)
        : base(rules, controllerType, selection)
    {
        _rules = rules;
        _controllerType = controllerType;
        _selection = selection;
    }

    /// <summary>Permit each type selected by this block to use its own contract.</summary>
    public void TranslateEachType() => Behavior.TranslateEachType();

    /// <summary>Use the generic response for all errors selected by this block.</summary>
    public void UseGenericProblem() => Behavior.UseGenericProblem();

    /// <summary>Search above this block for an authorized ancestor contract.</summary>
    public void UseNearestAncestorMapping() => Behavior.UseNearestAncestorMapping();

    private ErrorVisibilitySelection Behavior
    {
        get
        {
            if (field is not null)
                return field;

            var rule = new VisibilityRule(_selection, _controllerType);

            _rules.Add(rule);

            return field = new ErrorVisibilitySelection(rule);
        }
    }
}
