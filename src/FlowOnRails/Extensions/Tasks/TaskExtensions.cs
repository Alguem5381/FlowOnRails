namespace FlowOnRails.Extensions.Tasks;

using FlowOnRails;

/// <summary>
/// Provides extension methods for <see cref="Task{Rail}"/> to chain operations.
/// </summary>
public static class TaskExtensions
{
    extension(Task<Rail> task)
    {
        /// <summary>
        /// Matches the rail asynchronously and returns a value based on success or failure.
        /// </summary>
        /// <example>
        /// <code>
        /// return await GetResultAsync().MatchAsync(
        ///     onSuccess: () => Results.Ok(),
        ///     onFailure: error => Results.BadRequest(error)
        /// );
        /// </code>
        /// </example>
        public async Task<TNext> MatchAsync<TNext>(Func<TNext> onSuccess, Func<Error, TNext> onFailure)
            where TNext : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Match(onSuccess, onFailure);
        }

            /// <summary>
        /// Binds the rail to another asynchronous operation if successful.
        /// </summary>
        /// <example>
        /// <code>
        /// var rail = await PerformStep1Async()
        ///     .BindAsync(() => PerformStep2Async());
        /// </code>
        /// </example>
        public async Task<Rail> BindAsync(Func<Task<Rail>> bind)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return rail;

            var next = await bind().ConfigureAwait(false);
            return rail.Bind(() => next);
        }

        /// <summary>
        /// Binds the rail to another asynchronous operation if successful.
        /// </summary>
        public async Task<RailFor<TOut>> BindAsync<TOut>(Func<Task<RailFor<TOut>>> bind) where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return RailFor<TOut>.Fail(rail.Error!);

            var next = await bind().ConfigureAwait(false);
            return rail.Bind(() => next);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful, returning the original rail.
        /// </summary>
        public async Task<Rail> TapAsync(Action action)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Tap(action);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful, returning the original rail.
        /// </summary>
        public async Task<Rail> TapAsync(Func<Task> action)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess)
                await action().ConfigureAwait(false);

            return rail;
        }

        /// <summary>
        /// Ensures that the specified predicate is true asynchronously. If false, returns a failed rail with the provided error.
        /// </summary>
        public async Task<Rail> EnsureAsync(Func<bool> predicate, Error error)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Ensure(predicate, error);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful and the condition is true.
        /// </summary>
        public async Task<Rail> TapIfAsync(bool condition, Func<Task> action)
        {
            var rail = await task.ConfigureAwait(false);

            if (rail.IsSuccess && condition)
                await action().ConfigureAwait(false);

            return rail;
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful and the predicate evaluates to true.
        /// </summary>
        public async Task<Rail> TapIfAsync(Func<bool> predicate, Func<Task> action)
        {
            var rail = await task.ConfigureAwait(false);

            if (rail.IsSuccess && predicate())
                await action().ConfigureAwait(false);

            return rail;
        }

            /// <summary>
        /// Replaces a failed rail asynchronously with the rail returned by the fallback function.
        /// </summary>
        public async Task<Rail> RecoverWithAsync(Func<Error, Task<Rail>> fallback)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess)
                return rail;

            var next = await fallback(rail.Error!).ConfigureAwait(false);
            return rail.RecoverWith(_ => next);
        }

        /// <summary>
        /// Maps a successful rail to a new value asynchronously.
        /// </summary>
        public async Task<RailFor<TOut>> MapAsync<TOut>(Func<TOut> map) where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Map(map);
        }

        /// <summary>
        /// Maps a successful rail to a new value asynchronously.
        /// </summary>
        public async Task<RailFor<TOut>> MapAsync<TOut>(Func<Task<TOut>> map) where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return RailFor<TOut>.Fail(rail.Error!);

            var value = await map().ConfigureAwait(false);
            return rail.Map(() => value);
        }

        /// <summary>
        /// Maps the error of a failed rail to another error asynchronously.
        /// </summary>
        public async Task<Rail> MapErrorAsync(Func<Error, Error> errorMapper)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.MapError(errorMapper);
        }

        /// <summary>
        /// Maps the error of a failed rail to another error asynchronously.
        /// </summary>
        public async Task<Rail> MapErrorAsync(Func<Error, Task<Error>> errorMapper)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess)
                return rail;

            var error = await errorMapper(rail.Error!).ConfigureAwait(false);
            return rail.MapError(_ => error);
        }
    }
}
