namespace FlowOnRails.AspNetCore.Visibility;

/// <summary>Selects error types within a global, controller, or tree scope.</summary>
public class RuleScope
{
    private readonly List<Rule> _rules;
    private readonly Type? _controllerType;
    private readonly ErrorSelection _bounds;

    internal RuleScope(List<Rule> rules, Type? controllerType, ErrorSelection bounds)
    {
        _rules = rules;
        _controllerType = controllerType;
        _bounds = bounds;
    }

    /// <summary>Select only this error type.</summary>
    public RuleSelection For<TError>() where TError : Error =>
        Select(typeof(TError), ErrorRange.Exact);

    /// <summary>Select this error type and all its descendants.</summary>
    public RuleSelection ForTree<TError>() where TError : Error =>
        Select(typeof(TError), ErrorRange.Tree);

    /// <summary>Select descendants without selecting their root.</summary>
    public RuleSelection
        ForDescendantsOf<TError>()
        where TError : Error =>
        Select(typeof(TError), ErrorRange.Descendants);

    /// <summary>Configure a block bounded by this error type and its descendants.</summary>
    public void
        ForTree<TError>(
            Action<RuleTree> configure
        )
        where TError : Error =>
        ConfigureTree(typeof(TError), ErrorRange.Tree, configure);

    /// <summary>Configure a block bounded by the descendants, excluding the root.</summary>
    public void
        ForDescendantsOf<TError>(
            Action<RuleTree> configure
        )
        where TError : Error =>
        ConfigureTree(typeof(TError), ErrorRange.Descendants, configure);

    private RuleSelection Select(Type root, ErrorRange range)
    {
        var selection = ValidateSelection(root, range);

        var rule = new Rule(selection, _controllerType);

        _rules.Add(rule);

        return new RuleSelection(rule);
    }

    private void
        ConfigureTree(
            Type root,
            ErrorRange range,
            Action<RuleTree> configure
        )
    {
        ArgumentNullException.ThrowIfNull(configure);

        var bounds = ValidateSelection(root, range);

        configure(new RuleTree(_rules, _controllerType, bounds));
    }

    private ErrorSelection ValidateSelection(Type root, ErrorRange range)
    {
        var selection = new ErrorSelection(root, range);

        return !_bounds.ContainsSelection(selection)
            ? throw new ArgumentException(
                $"Selection '{root}' ({range}) is outside '{_bounds.Root}' ({_bounds.Range}).")
            : selection;
    }
}
