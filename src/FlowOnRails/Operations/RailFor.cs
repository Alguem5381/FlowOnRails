namespace FlowOnRails;

using System.Diagnostics.CodeAnalysis;

public readonly partial struct RailFor<TValue> where TValue : notnull
{
    /// <summary>
    /// Attempts to unwrap the value and error from the rail.
    /// Returns true if the operation was successful, populating the 'value' parameter.
    /// Returns false if the operation failed, populating the 'error' parameter.
    /// </summary>
    /// <example>
    /// <code>
    /// if (rail.TryUnwrap(out var user, out var error))
    /// {
    ///     Console.WriteLine($"Found user: {user.Name}");
    /// }
    /// else
    /// {
    ///     Console.WriteLine($"Error: {error}");
    /// }
    /// </code>
    /// </example>
    public bool TryUnwrap([NotNullWhen(true)] out TValue? value, [NotNullWhen(false)] out Error? error)
    {
        var isSuccess = IsSuccess;

        value = isSuccess ? Value : default;
        error = Error;

        return isSuccess;
    }

    /// <summary>
    /// Attempts to unwrap the value, error, and semantic success from the rail.
    /// </summary>
    /// <remarks>
    /// The neutral success is materialized only when this overload observes a successful rail.
    /// </remarks>
    public bool TryUnwrap(
        [NotNullWhen(true)] out TValue? value,
        [NotNullWhen(false)] out Error? error,
        [NotNullWhen(true)] out Success? success)
    {
        var isSuccess = IsSuccess;

        value = isSuccess ? Value : default;
        error = Error;
        success = isSuccess ? Success.Materialize(SemanticSuccess) : null;

        return isSuccess;
    }

    /// <summary>
    /// Ensures that the specified predicate is true. If it is false, returns a failed rail with the provided error.
    /// </summary>
    public RailFor<TValue> Ensure(Func<TValue, bool> predicate, Error error) =>
        IsFailure
            ? this
            : predicate(Value)
                ? this
                : Fail(error);

    /// <summary>
    /// Executes the given action if the rail is successful, passing the value.
    /// </summary>
    /// <param name="tap">The action to execute.</param>
    /// <returns>The original rail to allow chaining.</returns>
    public RailFor<TValue> Tap(Action<TValue> tap)
    {
        if (IsSuccess)
            tap(Value);

        return this;
    }

    /// <summary>
    /// Executes the given action if the rail is successful and the condition is met.
    /// </summary>
    public RailFor<TValue> TapIf(bool condition, Action<TValue> tap)
    {
        if (IsSuccess && condition)
            tap(Value);

        return this;
    }

    /// <summary>
    /// Executes the given action if the rail is successful and the predicate evaluates to true.
    /// </summary>
    public RailFor<TValue> TapIf(Func<TValue, bool> predicate, Action<TValue> tap)
    {
        if (IsSuccess && predicate(Value))
            tap(Value);

        return this;
    }

    /// <summary>
    /// Recovers from a failed rail by producing a value with the neutral semantic success.
    /// </summary>
    public RailFor<TValue> Recover(Func<Error, TValue> recover) =>
        IsSuccess
            ? this
            : CreateSuccess(recover(Error!), success: null);

    /// <summary>
    /// Replaces a failed rail with the rail returned by the fallback function.
    /// </summary>
    public RailFor<TValue> RecoverWith(Func<Error, RailFor<TValue>> fallback) =>
        IsSuccess
            ? this
            : fallback(Error!);

    /// <summary>
    /// Matches the rail and returns a value based on success or failure.
    /// </summary>
    /// <typeparam name="TNext">The return type.</typeparam>
    /// <param name="onSuccess">Function to execute with the inner value if successful.</param>
    /// <param name="onFailure">Function to execute with the error if failed.</param>
    /// <returns>The rail of either the onSuccess or onFailure function.</returns>
    /// <example>
    /// <code>
    /// return rail.Match(
    ///     onSuccess: user => Results.Ok(user),
    ///     onFailure: error => Results.BadRequest(error)
    /// );
    /// </code>
    /// </example>
    public TNext Match<TNext>(Func<TValue, TNext> onSuccess, Func<Error, TNext> onFailure) where TNext : notnull =>
        IsSuccess
            ? onSuccess(Value)
            : onFailure(Error!);

    /// <summary>
    /// Matches the rail and exposes its value with the semantic success, or its error.
    /// </summary>
    public TNext Match<TNext>(Func<TValue, Success, TNext> onSuccess, Func<Error, TNext> onFailure)
        where TNext : notnull =>
        IsSuccess
            ? onSuccess(Value, Success.Materialize(SemanticSuccess))
            : onFailure(Error!);

    /// <summary>
    /// Maps the rail to another rail using the given function if successful.
    /// </summary>
    /// <typeparam name="TResult">The type of the new value.</typeparam>
    /// <param name="map">The function to transform the inner value.</param>
    /// <returns>A new rail with the transformed value, or the original error if failed.</returns>
    /// <example>
    /// <code>
    /// var lengthResult = Rail.Ok("Hello").Map(str => str.Length);
    /// </code>
    /// </example>
    public RailFor<TResult> Map<TResult>(Func<TValue, TResult> map) where TResult : notnull =>
        IsFailure
            ? RailFor<TResult>.Fail(Error!)
            : RailFor<TResult>.CreateSuccess(map(Value), SemanticSuccess);

    /// <summary>
    /// Binds the rail to another rail using the given function if successful.
    /// </summary>
    /// <typeparam name="TResult">The type of the new value.</typeparam>
    /// <param name="bind">The function that returns a new rail.</param>
    /// <returns>The new rail if successful, or the original error if failed.</returns>
    /// <example>
    /// <code>
    /// var rail = GetUser(id)
    ///     .Bind(user => CheckUserPermissions(user));
    /// </code>
    /// </example>
    public RailFor<TResult> Bind<TResult>(Func<TValue, RailFor<TResult>> bind) where TResult : notnull
    {
        if (IsFailure)
            return RailFor<TResult>.Fail(Error!);

        var next = bind(Value);
        return next.IsFailure
            ? next
            : RailFor<TResult>.CreateSuccess(
                next.Value,
                SuccessComposition.Combine(SemanticSuccess, next.SemanticSuccess));
    }

    /// <summary>
    /// Binds the rail to another rail that does not return a value.
    /// </summary>
    public Rail Bind(Func<TValue, Rail> bind)
    {
        if (IsFailure)
            return Rail.Fail(Error!);

        var next = bind(Value);
        return next.IsFailure
            ? next
            : Rail.CreateSuccess(SuccessComposition.Combine(SemanticSuccess, next.SemanticSuccess));
    }

    /// <summary>
    /// Maps the error of a failed rail to another error synchronously.
    /// </summary>
    public RailFor<TValue> MapError(Func<Error, Error> errorMapper) =>
        IsSuccess
            ? this
            : Fail(errorMapper(Error!));
}
