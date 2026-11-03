namespace FlowOnRails.AspNetCore.Visibility;

/// <summary>Selects error types within a global, controller, or tree scope.</summary>
public class ErrorVisibilityRules
{
    private readonly List<VisibilityRule> _rules;
    private readonly Type? _controllerType;
    private readonly ErrorSelection _bounds;

    internal ErrorVisibilityRules(List<VisibilityRule> rules, Type? controllerType, ErrorSelection bounds)
    {
        _rules = rules;
        _controllerType = controllerType;
        _bounds = bounds;
    }

    /// <summary>Select only this error type.</summary>
    public ErrorVisibilitySelection For<TError>() where TError : Error => Select(typeof(TError), ErrorRange.Exact);

    /// <summary>Select this error type and all its descendants.</summary>
    public ErrorVisibilitySelection ForTree<TError>() where TError : Error => Select(typeof(TError), ErrorRange.Tree);

    /// <summary>Select descendants without selecting their root.</summary>
    public ErrorVisibilitySelection ForDescendantsOf<TError>() where TError : Error => Select(typeof(TError), ErrorRange.Descendants);

    /// <summary>Configure a block bounded by this error type and its descendants.</summary>
    public void ForTree<TError>(Action<ErrorVisibilityTree> configure) where TError : Error =>
        ConfigureTree(typeof(TError), ErrorRange.Tree, configure);

    /// <summary>Configure a block bounded by the descendants, excluding the root.</summary>
    public void ForDescendantsOf<TError>(Action<ErrorVisibilityTree> configure) where TError : Error =>
        ConfigureTree(typeof(TError), ErrorRange.Descendants, configure);

    private ErrorVisibilitySelection Select(Type root, ErrorRange range)
    {
        var selection = ValidateSelection(root, range);
        var rule = new VisibilityRule(selection, _controllerType);
        _rules.Add(rule);
        return new ErrorVisibilitySelection(rule);
    }

    private void ConfigureTree(Type root, ErrorRange range, Action<ErrorVisibilityTree> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var bounds = ValidateSelection(root, range);
        configure(new ErrorVisibilityTree(_rules, _controllerType, bounds));
    }

    private ErrorSelection ValidateSelection(Type root, ErrorRange range)
    {
        var selection = new ErrorSelection(root, range);
        if (!_bounds.Contains(selection))
            throw new ArgumentException($"Selection '{root}' ({range}) is outside '{_bounds.Root}' ({_bounds.Range}).");
        return selection;
    }
}
