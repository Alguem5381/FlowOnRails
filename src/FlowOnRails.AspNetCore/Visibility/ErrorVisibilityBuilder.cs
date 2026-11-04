namespace FlowOnRails.AspNetCore.Visibility;

/// <summary>Configures global visibility and overrides for individual controller types.</summary>
public sealed class ErrorVisibilityBuilder : ErrorVisibilityRules
{
    private readonly List<VisibilityRule> _rules;

    /// <summary>Create an empty configuration, which uses the generic response by default.</summary>
    public ErrorVisibilityBuilder() : this([]) { }

    private ErrorVisibilityBuilder(List<VisibilityRule> rules)
        : base(rules, null, new ErrorSelection(typeof(Error), ErrorRange.Tree)) => _rules = rules;

    /// <summary>Configure overrides for this exact controller type; its subclasses do not inherit them.</summary>
    public void ForController<TController>(Action<ErrorVisibilityRules> configure) where TController : class
    {
        ArgumentNullException.ThrowIfNull(configure);

        var controller = typeof(TController);

        ErrorVisibilityPolicy.ValidateController(controller);

        configure(new ErrorVisibilityRules(_rules, controller, new ErrorSelection(typeof(Error), ErrorRange.Tree)));
    }

    /// <summary>Validate the rules and build an immutable policy snapshot.</summary>
    /// <exception cref="ArgumentNullException"></exception>
    public ErrorVisibilityPolicy Build()
    {
        var rules = _rules.Select(rule => rule.Snapshot()).ToArray();

        foreach (var rule in rules)
            if (rule.Behavior is null)
                throw new InvalidOperationException(
                    $"No visibility behavior was specified for '{rule.Selection.Root}' ({rule.Selection.Range}).");

        foreach (var group in rules.GroupBy(rule => (rule.ControllerType, rule.Selection)))
            if (group.Select(rule => rule.Behavior).Distinct().Count() > 1)
                throw new InvalidOperationException(
                    $"Conflicting visibility rules for '{group.Key.Selection.Root}' ({group.Key.Selection.Range}).");

        foreach (var blocked in rules.Where(rule =>
                     rule.Behavior == ErrorVisibilityBehavior.GenericProblem &&
                     rule.Selection.Range != ErrorRange.Exact))

        foreach (var other in rules.Where(rule =>
                     rule.ControllerType == blocked.ControllerType &&
                     rule.Behavior != ErrorVisibilityBehavior.GenericProblem))
        {
            var sameRoot = blocked.Selection.Root == other.Selection.Root;

            var startsInside = !sameRoot && blocked.Selection.Matches(other.Selection.Root);

            var moreSpecificAtSameRoot = sameRoot && other.Selection.Range < blocked.Selection.Range;

            var overlapsBlockedRange = blocked.Selection.Matches(other.Selection.Root) ||
                                       other.Selection.Range != ErrorRange.Exact;

            if (startsInside || (moreSpecificAtSameRoot && overlapsBlockedRange))
                throw new InvalidOperationException(
                    $"Rule for '{other.Selection.Root}' contradicts the generic-response exclusion of '{blocked.Selection.Root}'.");
        }

        return new ErrorVisibilityPolicy([.. rules.DistinctBy(rule => (rule.ControllerType, rule.Selection))]);
    }
}
