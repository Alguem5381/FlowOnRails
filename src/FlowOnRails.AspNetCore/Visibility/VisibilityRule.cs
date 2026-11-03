namespace FlowOnRails.AspNetCore.Visibility;

internal enum ErrorRange { Exact, Descendants, Tree }

internal readonly record struct ErrorSelection(Type Root, ErrorRange Range)
{
    internal bool Matches(Type type) => Range switch
    {
        ErrorRange.Exact => type == Root,
        ErrorRange.Descendants => type != Root && Root.IsAssignableFrom(type),
        _ => Root.IsAssignableFrom(type)
    };

    internal bool Contains(ErrorSelection other)
    {
        if (other.Root == Root)
            return Range == ErrorRange.Tree || Range == other.Range;

        return Range != ErrorRange.Exact && Matches(other.Root);
    }
}

internal sealed class VisibilityRule(ErrorSelection selection, Type? controllerType)
{
    internal ErrorSelection Selection { get; } = selection;
    internal Type? ControllerType { get; } = controllerType;
    internal ErrorVisibilityBehavior? Behavior { get; private set; }

    internal void SetBehavior(ErrorVisibilityBehavior behavior)
    {
        if (Behavior is { } existing && existing != behavior)
            throw new InvalidOperationException($"Conflicting visibility behaviors for '{Selection.Root}' ({Selection.Range}).");
        Behavior = behavior;
    }

    internal VisibilityRule Snapshot() => new(Selection, ControllerType) { Behavior = Behavior };
}
