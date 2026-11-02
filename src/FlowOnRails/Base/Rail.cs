namespace FlowOnRails;

/// <summary>
/// Represents the rail of an operation that does not return a value.
/// </summary>
public readonly partial struct Rail : IEquatable<Rail>
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

    private Rail(bool isSuccess, Outcome? outcome)
    {
        RailState.EnsureValid(isSuccess, outcome);

        IsSuccess = isSuccess;
        _outcome = outcome;
    }

    internal static Rail CreateSuccess(Success? success) =>
        new(isSuccess: true, outcome: Success.Normalize(success));

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
    public static Rail Ok() => CreateSuccess(success: null);

    /// <summary>
    /// Creates a successful rail with the specified semantic success and no value.
    /// </summary>
    public static Rail Succeed(Success success)
    {
        ArgumentNullException.ThrowIfNull(success);
        return CreateSuccess(success);
    }

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
    public static Rail Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Rail(isSuccess: false, outcome: error);
    }

    /// <summary>
    /// Creates a successful rail containing the specified value.
    /// </summary>
    /// <param name="value">The value to wrap in the rail.</param>
    /// <returns>A successful <see cref="RailFor{TValue}"/>.</returns>
    public static RailFor<TValue> Ok<TValue>(TValue value) where TValue : notnull =>
        RailFor<TValue>.CreateSuccess(value, success: null);

    /// <summary>
    /// Creates a successful rail containing the specified value and semantic success.
    /// </summary>
    public static RailFor<TValue> Ok<TValue>(TValue value, Success success) where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(success);
        return RailFor<TValue>.CreateSuccess(value, success);
    }

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
    /// Creates a successful rail with the specified semantic success and no value as a Task.
    /// </summary>
    public static Task<Rail> SucceedAsync(Success success) => Task.FromResult(Succeed(success));

    /// <summary>
    /// Creates a successful rail containing the specified value as a Task.
    /// </summary>
    public static Task<RailFor<TValue>> OkAsync<TValue>(TValue value) where TValue : notnull =>
        Task.FromResult(Ok(value));

    /// <summary>
    /// Creates a successful rail containing the specified value and semantic success as a Task.
    /// </summary>
    public static Task<RailFor<TValue>> OkAsync<TValue>(TValue value, Success success) where TValue : notnull =>
        Task.FromResult(Ok(value, success));

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

    /// <summary>
    /// Indicates whether the current rail is equal to another rail.
    /// </summary>
    public bool Equals(Rail other)
    {
        var isSuccess = IsSuccess;

        return isSuccess == other.IsSuccess &&
               EqualityComparer<Outcome?>.Default.Equals(_outcome, other._outcome);
    }

    /// <summary>
    /// Indicates whether this instance and a specified object are equal.
    /// </summary>
    public override bool Equals(object? obj) =>
        obj is Rail other && Equals(other);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode()
    {
        var isSuccess = IsSuccess;
        return HashCode.Combine(isSuccess, _outcome);
    }

    /// <summary>
    /// Compares two rails for equality.
    /// </summary>
    public static bool operator ==(Rail left, Rail right) => left.Equals(right);

    /// <summary>
    /// Compares two rails for inequality.
    /// </summary>
    public static bool operator !=(Rail left, Rail right) => !(left == right);
}
