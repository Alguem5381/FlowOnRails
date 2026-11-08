namespace FlowOnRails.AspNetCore.Visibility;

/// <summary>Configures global visibility and overrides for individual controller types.</summary>
public sealed class VisibilityBuilder
{
    private readonly List<VisibilityRule> _rules;

    /// <summary>Create an empty configuration, which uses the generic response by default.</summary>
    public VisibilityBuilder() : this([]) { }

    private VisibilityBuilder(List<VisibilityRule> rules)
    {
        _rules = rules;
        Global = new(rules, null, new(typeof(Error), ErrorRange.Tree));
    }

    /// <summary>Configure visibility shared by all endpoints unless a local rule overrides it.</summary>
    public VisibilityScope Global { get; }

    /// <summary>Configure overrides for this exact controller type; its subclasses do not inherit them.</summary>
    public void ForController<TController>(Action<VisibilityScope> configure)
        where TController : class
    {
        ArgumentNullException.ThrowIfNull(configure);

        var controller = typeof(TController);

        VisibilityPolicy.ValidateController(controller);

        configure(new VisibilityScope(_rules, controller, new ErrorSelection(typeof(Error), ErrorRange.Tree)));
    }

    /// <summary>Validate the rules and build an immutable policy snapshot.</summary>
    public VisibilityPolicy Build()
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
                     rule.Behavior == VisibilityBehavior.GenericProblem &&
                     rule.Selection.Range != ErrorRange.Exact))

        foreach (var other in rules.Where(rule =>
                     rule.ControllerType == blocked.ControllerType &&
                     rule.Behavior != VisibilityBehavior.GenericProblem))
        {
            if (other.Selection.ReopensExcludedRange(blocked.Selection))
                throw new InvalidOperationException(
                    $"Rule for '{other.Selection.Root}' contradicts the generic-response " +
                    $"exclusion of '{blocked.Selection.Root}'.");
        }

        return new VisibilityPolicy([.. rules.DistinctBy(rule => (rule.ControllerType, rule.Selection))]);
    }
}
