namespace FlowOnRails;

/// <summary>
/// Represents an error in a <see cref="Rail"/> or <see cref="RailFor{TValue}"/>.
/// Derive a record to define errors belonging to the consuming domain.
/// </summary>
public abstract record Error;
