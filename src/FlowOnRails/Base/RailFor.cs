using System.Diagnostics.CodeAnalysis;

namespace FlowOnRails;

/// <summary>
/// Represents the rail of an operation that returns a value.
/// </summary>
/// <typeparam name="TValue">The type of the value returned by the operation.</typeparam>
public readonly partial struct RailFor<TValue> : IRail, IEquatable<RailFor<TValue>> where TValue : notnull
{
    /// <inheritdoc/>
    public bool IsSuccess { get; }

    /// <inheritdoc/>
    public bool IsFailure { get => !IsSuccess; }

    /// <summary>
    /// Gets the error associated with a failed rail. Null if the operation was successful.
    /// </summary>
    internal Error? Error { get; }

    /// <summary>
    /// Gets the value associated with a successful rail.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if accessed on a failed rail.</exception>
    internal TValue Value { get; }

    private RailFor(bool isSuccess, Error? error, TValue? value)
    {
        if (isSuccess && value is null)
            throw new ArgumentNullException(nameof(value), "A success rail should contain a non-null value.");

        Value = value!;
        Error = error;
        IsSuccess = isSuccess;
    }

    /// <summary>
    /// Creates a successful rail containing the specified value.
    /// </summary>
    /// <param name="value">The value to wrap in the rail.</param>
    /// <returns>A successful <see cref="RailFor{TValue}"/>.</returns>
    /// <example>
    /// <code>
    /// var rail = RailFor&lt;int&gt;.Ok(42);
    /// if (rail.TryUnwrap(out var value, out var error))
    /// {
    ///     Console.WriteLine($"Success: {value}");
    /// }
    /// </code>
    /// </example>
    public static RailFor<TValue> Ok(TValue value) => new(isSuccess: true, error: null, value);

    /// <summary>
    /// Creates a failed rail with the specified error.
    /// </summary>
    /// <param name="error">The error that caused the failure.</param>
    /// <returns>A failed <see cref="RailFor{TValue}"/>.</returns>
    /// <example>
    /// <code>
    /// public record NotFoundError(string Message) : Error;
    /// 
    /// return RailFor&lt;User&gt;.Fail(new NotFoundError("User not found"));
    /// </code>
    /// </example>
    public static RailFor<TValue> Fail(Error error) => new(isSuccess: false, error: error, default);

    /// <summary>
    /// Implicitly converts a value into a successful rail.
    /// </summary>
    /// <param name="value">The value.</param>
    public static implicit operator RailFor<TValue>(TValue value) => Ok(value);

    /// <summary>
    /// Implicitly converts an error into a failed rail.
    /// </summary>
    /// <param name="error">The error.</param>
    public static implicit operator RailFor<TValue>(Error error) => Fail(error);

    bool IRail.TryGetError([NotNullWhen(true)] out Error? error)
    {
        error = Error;
        return IsFailure;
    }

    /// <summary>
    /// Indicates whether the current rail is equal to another rail.
    /// </summary>
    public bool Equals(RailFor<TValue> other)
    {
        if (IsSuccess != other.IsSuccess) return false;
        if (IsSuccess) return EqualityComparer<TValue>.Default.Equals(Value, other.Value);
        return EqualityComparer<Error?>.Default.Equals(Error, other.Error);
    }

    /// <summary>
    /// Indicates whether this instance and a specified object are equal.
    /// </summary>
    public override bool Equals(object? obj) => obj is RailFor<TValue> other && Equals(other);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode() =>
        IsSuccess ? HashCode.Combine(IsSuccess, Value) : HashCode.Combine(IsSuccess, Error);

    /// <summary>
    /// Compares two rails for equality.
    /// </summary>
    public static bool operator ==(RailFor<TValue> left, RailFor<TValue> right) => left.Equals(right);

    /// <summary>
    /// Compares two rails for inequality.
    /// </summary>
    public static bool operator !=(RailFor<TValue> left, RailFor<TValue> right) => !(left == right);
}
