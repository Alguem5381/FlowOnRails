using System.Diagnostics.CodeAnalysis;

namespace FlowOnRails;

/// <summary>
/// Represents a non-generic interface for a rail operation.
/// </summary>
public interface IRail
{
    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    bool IsSuccess { get; }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    bool IsFailure { get; }

    /// <summary>
    /// Attempts to get the error if the rail is a failure.
    /// Returns true if there is a failure, and populates the 'error' parameter.
    /// </summary>
    bool TryGetError([NotNullWhen(true)] out Error? error);
}
