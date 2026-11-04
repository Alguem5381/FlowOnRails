namespace FlowOnRails.AspNetCore.Visibility;

internal enum ErrorRange { Exact, Descendants, Tree }

internal readonly record struct ErrorSelection(Type Root, ErrorRange Range)
{
    internal bool IncludesType(Type type) => Range switch
    {
        ErrorRange.Exact => type == Root,
        ErrorRange.Descendants => type != Root && Root.IsAssignableFrom(type),
        ErrorRange.Tree => Root.IsAssignableFrom(type),
        _ => throw new ArgumentOutOfRangeException(nameof(Range), Range, "Unknown error selection range.")
    };

    internal bool ContainsSelection(ErrorSelection other)
    {
        if (other.Root == Root)
            return Range == ErrorRange.Tree || Range == other.Range;

        return IncludesType(other.Root);
    }

    internal bool ReopensExcludedRange(ErrorSelection blocked)
    {
        if (blocked.Root != Root)
            return blocked.IncludesType(Root);

        return blocked.Range == ErrorRange.Tree &&
               Range is ErrorRange.Exact or ErrorRange.Descendants;
    }
}

internal sealed class VisibilityRule(ErrorSelection selection, Type? controllerType)
{
    internal ErrorSelection Selection { get; } = selection;
    internal Type? ControllerType { get; } = controllerType;
    internal VisibilityBehavior? Behavior { get; private set; }

    internal void SetBehavior(VisibilityBehavior behavior)
    {
        if (Behavior is { } existing && existing != behavior)
            throw new InvalidOperationException(
                $"Conflicting visibility behaviors for '{Selection.Root}' ({Selection.Range}).");

        Behavior = behavior;
    }

    internal VisibilityRule
        Snapshot(
        ) => new(Selection, ControllerType) { Behavior = Behavior };
}
