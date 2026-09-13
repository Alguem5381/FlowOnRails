namespace FlowOnRails;

using System.Diagnostics.CodeAnalysis;

public readonly partial struct Rail
{
    /// <summary>
    /// Attempts to unwrap the error from the rail.
    /// Returns true if the operation was successful.
    /// Returns false if the operation failed, populating the 'error' parameter.
    /// </summary>
    /// <example>
    /// <code>
    /// if (!rail.TryUnwrap(out var error))
    /// {
    ///     Console.WriteLine($"Failed: {error}");
    /// }
    /// </code>
    /// </example>
    public bool TryUnwrap([NotNullWhen(false)] out Error
        ? error)
    {
        error = Error!;
        return IsSuccess;
    }

    /// <summary>
    /// Executes the given action if the rail is successful.
    /// </summary>
    /// <param name="tap">The action to execute.</param>
    /// <returns>The original rail to allow chaining.</returns>
    /// <example>
    /// <code>
    /// rail.Tap(() => Console.WriteLine("Action succeeded!"));
    /// </code>
    /// </example>
    public Rail Tap(Action tap)
    {
        if (IsSuccess)
            tap();

        return this;
    }

    /// <summary>
    /// Executes the given action if the rail is successful and the condition is met.
    /// </summary>
    public Rail TapIf(bool condition, Action tap)
    {
        if (IsSuccess && condition)
            tap();

        return this;
    }

    /// <summary>
    /// Executes the given action if the rail is successful and the predicate evaluates to true.
    /// </summary>
    public Rail TapIf(Func<bool> predicate, Action tap)
    {
        if (IsSuccess && predicate())
            tap();

        return this;
    }

    /// <summary>
    /// Recovers from a failed rail by executing the fallback function.
    /// </summary>
    /// <example>
    /// <code>
    /// var rail = ExecuteOperation()
    ///     .Recover(error => Rail.Ok()); // Fallback to success
    /// </code>
    /// </example>
    public Rail Recover(Func<Error, Rail> fallback) =>
        IsSuccess
            ? this
            : fallback(Error!);

    /// <summary>
    /// Binds the rail to another rail using the given function if successful.
    /// </summary>
    /// <param name="bind">The function to execute on success.</param>
    /// <returns>The new rail if successful; otherwise, a failed rail with the original error.</returns>
    /// <example>
    /// <code>
    /// var finalRail = ValidateUser()
    ///     .Bind(() => SaveUserToDatabase());
    /// </code>
    /// </example>
    public Rail Bind(Func<Rail> bind) =>
        IsSuccess
            ? bind()
            : new Rail(isSuccess: false, Error);


    /// <summary>
    /// Ensures that the specified predicate is true. If it is false, returns a failed rail with the provided error.
    /// </summary>
    public Rail Ensure(Func<bool> predicate, Error error)
    {
        if (IsFailure)
            return this;

        return predicate()
            ? this
            : Fail(error);
    }

    /// <summary>
    /// Matches the rail and returns a value based on success or failure.
    /// </summary>
    /// <typeparam name="TNext">The return type.</typeparam>
    /// <param name="onSuccess">Function to execute if successful.</param>
    /// <param name="onFailure">Function to execute if failed.</param>
    /// <example>
    /// <code>
    /// string message = rail.Match(
    ///     onSuccess: () => "Operation was successful!",
    ///     onFailure: error => $"Operation failed: {error.Message}"
    /// );
    /// </code>
    /// </example>
    public TNext Match<TNext>(Func<TNext> onSuccess, Func<Error, TNext> onFailure)
    {
        return IsSuccess
            ? onSuccess()
            : onFailure(Error!);
    }

    /// <summary>
    /// Binds the rail to another rail that returns a value using the given function if successful.
    /// </summary>
    public RailFor<TNext> Bind<TNext>(Func<RailFor<TNext>> bind) where TNext : notnull =>
        IsSuccess
            ? bind()
            : RailFor<TNext>.Fail(Error!);

    /// <summary>
    /// Maps a successful rail to a rail with a value.
    /// </summary>
    public RailFor<TNext> Map<TNext>(Func<TNext> map) where TNext : notnull =>
        IsSuccess
            ? RailFor<TNext>.Ok(map())
            : RailFor<TNext>.Fail(Error!);

    /// <summary>
    /// Maps the error of a failed rail to another error synchronously.
    /// </summary>
    public Rail MapError(Func<Error, Error> errorMapper) =>
        IsSuccess
            ? this
            : Fail(errorMapper(Error!));
}
