namespace FlowOnRails;

/// <summary>
/// Represents the semantic success of a rail operation.
/// </summary>
/// <remarks>
/// Derive a record from this type when a successful operation has domain meaning beyond its value.
/// Rails created without an explicit success use a shared neutral success when that outcome is observed.
/// Passing that neutral success, or a record copy of it, back to a rail factory preserves neutrality.
/// </remarks>
public abstract record Success : Outcome
{
    private sealed record NeutralSuccess : Success;

    private static class NeutralHolder
    {
        internal static readonly Success Instance;

        static NeutralHolder()
        {
            Instance = new NeutralSuccess();
        }
    }

    internal static Success Neutral => NeutralHolder.Instance;

    /// <summary>
    /// Initializes a domain success.
    /// </summary>
    protected Success()
    {
    }

    // Recognize copies made with `with` without treating domain successes as neutral.
    internal static Success? Normalize(Success? success) => success is NeutralSuccess ? null : success;

    internal static Success Materialize(Success? success) => success ?? Neutral;
}
