using System.Diagnostics.CodeAnalysis;

namespace FlowOnRails;

/// <summary>
/// Represents the rail of an operation that does not return a value.
/// </summary>
public readonly partial struct Rail : IRail, IEquatable<Rail>
{
    /// <inheritdoc/>
    public bool IsSuccess { get; }

    /// <inheritdoc/>
    public bool IsFailure { get => !IsSuccess; }

    /// <summary>
    /// Gets the error associated with a failed rail. Null if the operation was successful.
    /// </summary>
    internal Error? Error { get; }

    private Rail(bool isSuccess, Error? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>
    /// Creates a successful rail.
    /// </summary>
    /// <returns>A successful <see cref="Rail"/>.</returns>
    /// <example>
    /// <code>
    /// var rail = Rail.Ok();
    /// if (!rail.TryUnwrap(out var error)) 
    /// {
    ///     Console.WriteLine($"Failed with error: {error}");
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// Always extract and check for errors using <c>TryUnwrap</c>.
    /// </remarks>
    public static Rail Ok() => new(isSuccess: true, error: null);

    /// <summary>
    /// Creates a failed rail with the specified error.
    /// </summary>
    /// <param name="error">The <see cref="Error"/> that caused the failure.</param>
    /// <returns>A failed <see cref="Rail"/>.</returns>
    /// <example>
    /// <code>
    /// public record ValidationError(string Message) : Error;
    /// 
    /// var error = new ValidationError("Invalid input");
    /// return Rail.Fail(error);
    /// </code>
    /// </example>
    public static Rail Fail(Error error) => new(isSuccess: false, error: error);

    /// <summary>
    /// Creates a successful rail containing the specified value.
    /// </summary>
    /// <param name="value">The value to wrap in the rail.</param>
    /// <returns>A successful <see cref="RailFor{TValue}"/>.</returns>
    public static RailFor<TValue> Ok<TValue>(TValue value) where TValue : notnull => RailFor<TValue>.Ok(value);

    /// <summary>
    /// Creates a failed rail with the specified error.
    /// </summary>
    /// <param name="error">The error that caused the failure.</param>
    /// <returns>A failed <see cref="RailFor{TValue}"/>.</returns>
    public static RailFor<TValue> Fail<TValue>(Error error) where TValue : notnull => RailFor<TValue>.Fail(error);

    /// <summary>
    /// Creates a successful rail as a Task.
    /// </summary>
    public static Task<Rail> OkAsync() => Task.FromResult(Ok());

    /// <summary>
    /// Creates a successful rail containing the specified value as a Task.
    /// </summary>
    public static Task<RailFor<TValue>> OkAsync<TValue>(TValue value) where TValue : notnull =>
        Task.FromResult(Ok(value));

    /// <summary>
    /// Creates a failed rail with the specified error as a Task.
    /// </summary>
    public static Task<Rail> FailAsync(Error error) => Task.FromResult(Fail(error));

    /// <summary>
    /// Creates a failed rail with the specified error as a Task.
    /// </summary>
    public static Task<RailFor<TValue>> FailAsync<TValue>(Error error) where TValue : notnull =>
        Task.FromResult(Fail<TValue>(error));

    /// <summary>
    /// Implicitly converts an error into a failed rail.
    /// </summary>
    /// <param name="error">The error.</param>
    public static implicit operator Rail(Error error) => Fail(error: error);

    bool IRail.TryGetError([NotNullWhen(true)] out Error? error)
    {
        error = Error;
        return IsFailure;
    }

    /// <summary>
    /// Indicates whether the current rail is equal to another rail.
    /// </summary>
    public bool Equals(Rail other)
    {
        if (IsSuccess != other.IsSuccess) return false;
        return IsSuccess ||
               EqualityComparer<Error?>.Default.Equals(Error, other.Error);
    }

    /// <summary>
    /// Indicates whether this instance and a specified object are equal.
    /// </summary>
    public override bool Equals(object? obj) => obj is Rail other && Equals(other);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode() =>
        IsSuccess
            ? HashCode.Combine(IsSuccess)
            : HashCode.Combine(IsSuccess, Error);

    /// <summary>
    /// Compares two rails for equality.
    /// </summary>
    public static bool operator ==(Rail left, Rail right) => left.Equals(right);

    /// <summary>
    /// Compares two rails for inequality.
    /// </summary>
    public static bool operator !=(Rail left, Rail right) => !(left == right);
}
