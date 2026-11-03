namespace FlowOnRails;

internal static class RailState
{
    internal static void EnsureValid(bool isSuccess, Error? error)
    {
        if (!IsValid(isSuccess, error))
            throw new ArgumentException("The error does not match the rail state.", nameof(error));
    }

    internal static void EnsureInitialized(bool isSuccess, Error? error)
    {
        if (!IsValid(isSuccess, error))
            throw new InvalidOperationException(
                "The Rail is uninitialized or invalid. " +
                "Create rails using Rail.Ok(...) or Rail.Fail(...).");
    }

    private static bool IsValid(bool isSuccess, Error? error) => isSuccess == (error is null);
}
