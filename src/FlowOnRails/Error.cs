namespace FlowOnRails;

/// <summary>
/// Represents an error in a <see cref="Rail"/> or <see cref="RailFor{TValue}"/>.
/// </summary>
public abstract record Error : Outcome
{
    /// <summary>
    /// Initializes a domain error.
    /// </summary>
    protected Error()
    {
    }
}
