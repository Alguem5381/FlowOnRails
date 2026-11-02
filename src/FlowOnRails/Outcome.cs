namespace FlowOnRails;

/// <summary>
/// Represents the semantic outcome of a rail operation.
/// </summary>
/// <remarks>
/// Consumers extend either <see cref="Success"/> or <see cref="Error"/>.
/// Direct inheritance is restricted to this assembly so the two outcome branches remain closed.
/// </remarks>
public abstract record Outcome
{
    private protected Outcome()
    {
    }
}
