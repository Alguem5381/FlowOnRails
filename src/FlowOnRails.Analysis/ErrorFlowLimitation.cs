using System;

namespace FlowOnRails.Analysis;

/// <summary>A classified analysis limit with a human-readable explanation.</summary>
public sealed class ErrorFlowLimitation : IEquatable<ErrorFlowLimitation>
{
    /// <summary>Gets the stable reason. Unclassified preserves legacy or unrecognized reasons.</summary>
    public ErrorFlowLimitationCode Code { get; }

    /// <summary>Gets the explanation for presentation; its wording is not a stable identifier.</summary>
    public string Message { get; }

    internal ErrorFlowLimitation(ErrorFlowLimitationCode code, string message) =>
        (Code, Message) = (code, message);

    /// <summary>Compares both the reason and its specific explanation.</summary>
    public bool Equals(ErrorFlowLimitation? other) =>
        other is not null && Code == other.Code && StringComparer.Ordinal.Equals(Message, other.Message);

    /// <inheritdoc />
    public override bool Equals(object? other) => other is ErrorFlowLimitation limitation && Equals(limitation);

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((int)Code * 397 ^ StringComparer.Ordinal.GetHashCode(Message));

    /// <summary>Returns the human-readable explanation.</summary>
    public override string ToString() => Message;
}
