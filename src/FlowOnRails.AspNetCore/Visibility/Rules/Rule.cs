namespace FlowOnRails.AspNetCore.Visibility;

internal sealed class Rule(ErrorSelection selection, Type? controllerType)
{
    internal ErrorSelection Selection { get; } = selection;
    internal Type? ControllerType { get; } = controllerType;
    internal RuleBehavior? Behavior { get; private set; }

    internal void SetBehavior(RuleBehavior behavior)
    {
        if (Behavior is { } existing &&
            existing != behavior)
            throw new InvalidOperationException(
                $"Conflicting visibility behaviors for '{Selection.Root}' ({Selection.Range}).");

        Behavior = behavior;
    }

    internal Rule Snapshot() =>
        new(Selection, ControllerType) { Behavior = Behavior };
}
