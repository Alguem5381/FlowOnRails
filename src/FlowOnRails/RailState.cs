namespace FlowOnRails;

internal static class RailState
{
    internal static void EnsureValid(bool isSuccess, Outcome? outcome)
    {
        if (!IsValid(isSuccess, outcome))
            throw new ArgumentException("The outcome does not match the rail state.", nameof(outcome));
    }

    internal static void EnsureInitialized(bool isSuccess, Outcome? outcome)
    {
        if (!IsValid(isSuccess, outcome))
            throw new InvalidOperationException(
                "The Rail is uninitialized or invalid. " +
                "Create rails using Rail.Ok(...), Rail.Succeed(...), or Rail.Fail(...).");
    }

    private static bool IsValid(bool isSuccess, Outcome? outcome) =>
        (isSuccess && outcome is null or Success) ||
        (!isSuccess && outcome is Error);
}
