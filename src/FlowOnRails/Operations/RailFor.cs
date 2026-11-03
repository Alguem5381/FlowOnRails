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
    /// Ensures that the specified predicate is true. If it is false, returns a failed rail with the provided error.
    /// </summary>
    public RailFor<TValue> Ensure(Func<TValue, bool> predicate, Error error) =>
        IsFailure
            ? this
            : predicate(Value)
                ? this
                : Fail(error);

    /// <summary>
    /// Ensures the predicate holds, creating an error only when a successful rail fails the predicate.
    /// </summary>
    /// <remarks>Existing failures skip both callbacks. A null produced error is rejected; callback exceptions propagate.</remarks>
    public RailFor<TValue> Ensure(Func<TValue, bool> predicate, Func<TValue, Error> errorFactory)
    {
        if (IsFailure)
            return this;

        return predicate(Value)
            ? this
            : Fail(errorFactory(Value));
    }

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
    /// Observes the original error on failure, preserving the rail.
    /// </summary>
    /// <remarks>Successful rails skip the callback. Callback exceptions propagate.</remarks>
    public RailFor<TValue> TapError(Action<Error> tap)
    {
        if (IsFailure)
            tap(Error);

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
    /// Recovers from a failed rail by producing a value.
    /// </summary>
    public RailFor<TValue> Recover(Func<Error, TValue> recover) =>
        IsSuccess
            ? this
            : CreateSuccess(recover(Error));

    /// <summary>
    /// Replaces a failed rail with the rail returned by the fallback function.
    /// </summary>
    public RailFor<TValue> RecoverWith(Func<Error, RailFor<TValue>> fallback)
    {
        if (IsSuccess)
            return this;

        var next = fallback(Error);
        _ = next.IsSuccess;
        return next;
    }

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
            : onFailure(Error);

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
            ? RailFor<TResult>.Fail(Error)
            : RailFor<TResult>.CreateSuccess(map(Value));

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
            return RailFor<TResult>.Fail(Error);

        var next = bind(Value);
        _ = next.IsSuccess;
        return next;
    }

    /// <summary>
    /// Binds the rail to another rail that does not return a value.
    /// </summary>
    public Rail Bind(Func<TValue, Rail> bind)
    {
        if (IsFailure)
            return Rail.Fail(Error);

        var next = bind(Value);
        _ = next.IsSuccess;
        return next;
    }

    /// <summary>
    /// Executes a fallible operation on success, preserving the original value if it succeeds.
    /// </summary>
    /// <param name="check">The operation to check using the current value.</param>
    /// <returns>The original rail on success, or a failed rail containing the checked operation's error.</returns>
    /// <remarks>Existing failures skip the callback. Uninitialized rails throw InvalidOperationException; callback exceptions propagate.</remarks>
    public RailFor<TValue> Check(Func<TValue, Rail> check)
    {
        if (IsFailure)
            return this;

        var next = check(Value);
        return next.IsFailure ? Fail(next.Error) : this;
    }

    /// <summary>
    /// Executes a fallible operation on success, discarding its value and preserving the original value if it succeeds.
    /// </summary>
    /// <typeparam name="TChecked">The type of the discarded value.</typeparam>
    /// <param name="check">The operation to check using the current value.</param>
    /// <returns>The original rail on success, or a failed rail containing the checked operation's error.</returns>
    /// <remarks>Existing failures skip the callback. Uninitialized rails throw InvalidOperationException; callback exceptions propagate.</remarks>
    public RailFor<TValue> Check<TChecked>(Func<TValue, RailFor<TChecked>> check) where TChecked : notnull
    {
        if (IsFailure)
            return this;

        var next = check(Value);
        return next.IsFailure ? Fail(next.Error) : this;
    }

    /// <summary>
    /// Maps the error of a failed rail to another error synchronously.
    /// </summary>
    public RailFor<TValue> MapError(Func<Error, Error> errorMapper) =>
        IsSuccess
            ? this
            : Fail(errorMapper(Error));
}
