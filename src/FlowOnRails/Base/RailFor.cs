namespace FlowOnRails;

/// <summary>
/// Represents the rail of an operation that returns a value.
/// </summary>
/// <typeparam name="TValue">The type of the value returned by the operation.</typeparam>
public readonly partial struct RailFor<TValue> : IEquatable<RailFor<TValue>> where TValue : notnull
{
    private readonly Outcome? _outcome;

    /// <summary>
    /// Gets a value indicating whether the operation was successful.
    /// </summary>
    public bool IsSuccess
    {
        get
        {
            RailState.EnsureInitialized(field, _outcome);
            return field;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the error associated with a failed rail. Null if the operation was successful.
    /// </summary>
    internal Error? Error => _outcome as Error;

    /// <summary>
    /// Gets the explicit semantic success, or null when the neutral success is being used.
    /// </summary>
    internal Success? SemanticSuccess => _outcome as Success;

    /// <summary>
    /// Gets the semantic outcome of the operation.
    /// </summary>
    /// <remarks>
    /// Reading this property materializes the shared neutral success when no rich success was supplied.
    /// </remarks>
    public Outcome Outcome
    {
        get
        {
            _ = IsSuccess;
            return _outcome ?? Success.Neutral;
        }
    }

    /// <summary>
    /// Gets the value associated with a successful rail.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if accessed on a failed rail.</exception>
    internal TValue Value { get; }

    private RailFor(bool isSuccess, Outcome? outcome, TValue? value)
    {
        if (isSuccess && value is null)
            throw new ArgumentNullException(nameof(value), "A success rail should contain a non-null value.");

        RailState.EnsureValid(isSuccess, outcome);

        Value = value!;
        _outcome = outcome;
        IsSuccess = isSuccess;
    }

    internal static RailFor<TValue> CreateSuccess(TValue value, Success? success) =>
        new(isSuccess: true, outcome: Success.Normalize(success), value);

    /// <summary>
    /// Creates a failed rail with the specified error.
    /// </summary>
    /// <param name="error">The error that caused the failure.</param>
    /// <returns>A failed <see cref="RailFor{TValue}"/>.</returns>
    /// <example>
    /// <code>
    /// public record NotFoundError(string Message) : Error;
    /// 
    /// return Rail.Fail&lt;User&gt;(new NotFoundError("User not found"));
    /// </code>
    /// </example>
    internal static RailFor<TValue> Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new RailFor<TValue>(isSuccess: false, outcome: error, value: default);
    }

    /// <summary>
    /// Implicitly converts a value into a successful rail.
    /// </summary>
    /// <param name="value">The value.</param>
    public static implicit operator RailFor<TValue>(TValue value) => CreateSuccess(value, success: null);

    /// <summary>
    /// Implicitly converts an error into a failed rail.
    /// </summary>
    /// <param name="error">The error.</param>
    public static implicit operator RailFor<TValue>(Error error) => Fail(error);

    /// <summary>
    /// Indicates whether the current rail is equal to another rail.
    /// </summary>
    public bool Equals(RailFor<TValue> other)
    {
        var isSuccess = IsSuccess;

        if (isSuccess != other.IsSuccess)
            return false;

        if (!EqualityComparer<Outcome?>.Default.Equals(_outcome, other._outcome))
            return false;

        return !isSuccess || EqualityComparer<TValue>.Default.Equals(Value, other.Value);
    }

    /// <summary>
    /// Indicates whether this instance and a specified object are equal.
    /// </summary>
    public override bool Equals(object? obj) => obj is RailFor<TValue> other && Equals(other);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode()
    {
        var isSuccess = IsSuccess;
        return isSuccess
            ? HashCode.Combine(isSuccess, Value, _outcome)
            : HashCode.Combine(isSuccess, _outcome);
    }

    /// <summary>
    /// Compares two rails for equality.
    /// </summary>
    public static bool operator ==(RailFor<TValue> left, RailFor<TValue> right) => left.Equals(right);

    /// <summary>
    /// Compares two rails for inequality.
    /// </summary>
    public static bool operator !=(RailFor<TValue> left, RailFor<TValue> right) => !(left == right);
}
