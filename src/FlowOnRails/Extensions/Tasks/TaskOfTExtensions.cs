namespace FlowOnRails.Extensions.Tasks;

using FlowOnRails;

/// <summary>
/// Provides extension methods for <see cref="Task{TResult}"/> to chain operations.
/// </summary>
public static class TaskOfTExtensions
{
    extension<TValue>(Task<RailFor<TValue>> task) where TValue : notnull
    {
        /// <summary>
        /// Awaits the source and creates an error only if the predicate fails on success.
        /// </summary>
        public async Task<RailFor<TValue>> EnsureAsync(Func<TValue, bool> predicate, Func<TValue, Error> errorFactory)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Ensure(predicate, errorFactory);
        }

        /// <summary>
        /// Observes the original error on failure after awaiting the source, preserving the rail.
        /// </summary>
        public async Task<RailFor<TValue>> TapErrorAsync(Action<Error> action)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.TapError(action);
        }

        /// <summary>
        /// Awaits an error observer on failure, preserving the rail.
        /// </summary>
        /// <remarks>Successful rails skip the callback. Exceptions and cancellation propagate.</remarks>
        public async Task<RailFor<TValue>> TapErrorAsync(Func<Error, Task> action)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                await action(rail.Error).ConfigureAwait(false);

            return rail;
        }

        /// <summary>
        /// Checks another operation on success, preserving the source value, if any, and propagating failure.
        /// </summary>
        public async Task<RailFor<TValue>> CheckAsync(Func<TValue, Rail> check)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Check(check);
        }

        /// <summary>
        /// Checks another operation on success, preserving the source value, if any, and propagating failure.
        /// </summary>
        public async Task<RailFor<TValue>> CheckAsync(Func<TValue, Task<Rail>> check)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure) return rail;
            var next = await check(rail.Value).ConfigureAwait(false);
            return rail.Check(_ => next);
        }

        /// <summary>
        /// Checks another operation on success, preserving the source value, if any, and propagating failure.
        /// </summary>
        public async Task<RailFor<TValue>> CheckAsync<TChecked>(Func<TValue, RailFor<TChecked>> check)
            where TChecked : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Check(check);
        }

        /// <summary>
        /// Checks another operation on success, preserving the source value, if any, and propagating failure.
        /// </summary>
        public async Task<RailFor<TValue>> CheckAsync<TChecked>(Func<TValue, Task<RailFor<TChecked>>> check)
            where TChecked : notnull
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure) return rail;
            var next = await check(rail.Value).ConfigureAwait(false);
            return rail.Check(_ => next);
        }

        /// <summary>
        /// Matches the rail asynchronously and returns a value based on success or failure.
        /// </summary>
        public async Task<TNext> MatchAsync<TNext>(Func<TValue, TNext> onSuccess, Func<Error, TNext> onFailure)
            where TNext : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Match(onSuccess, onFailure);
        }

            /// <summary>
        /// Binds the rail to another asynchronous operation if successful.
        /// </summary>
        public async Task<RailFor<TOut>> BindAsync<TOut>(Func<TValue, Task<RailFor<TOut>>> bind)
            where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return RailFor<TOut>.Fail(rail.Error);

            var next = await bind(rail.Value).ConfigureAwait(false);
            return rail.Bind(_ => next);
        }

        /// <summary>
        /// Binds a task-based rail to a ValueTask callback, returning a Task.
        /// </summary>
        /// <remarks>
        /// The distinct name keeps async lambdas passed to BindAsync unambiguous.
        /// </remarks>
        public async Task<RailFor<TOut>> BindValueTaskAsync<TOut>(Func<TValue, ValueTask<RailFor<TOut>>> bind)
            where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return RailFor<TOut>.Fail(rail.Error);

            var next = await bind(rail.Value).ConfigureAwait(false);
            return rail.Bind(_ => next);
        }

        /// <summary>
        /// Binds the rail to another asynchronous operation if successful.
        /// </summary>
        public async Task<Rail> BindAsync(Func<TValue, Task<Rail>> bind)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return Rail.Fail(rail.Error);

            var next = await bind(rail.Value).ConfigureAwait(false);
            return rail.Bind(_ => next);
        }

        /// <summary>
        /// Binds a task-based rail to a ValueTask callback, returning a Task.
        /// </summary>
        /// <remarks>
        /// The distinct name keeps async lambdas passed to BindAsync unambiguous.
        /// </remarks>
        public async Task<Rail> BindValueTaskAsync(Func<TValue, ValueTask<Rail>> bind)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return Rail.Fail(rail.Error);

            var next = await bind(rail.Value).ConfigureAwait(false);
            return rail.Bind(_ => next);
        }

        /// <summary>
        /// Maps the inner value of a successful rail to a new value asynchronously.
        /// </summary>
        public async Task<RailFor<TOut>> MapAsync<TOut>(Func<TValue, TOut> map) where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Map(map);
        }

        /// <summary>
        /// Maps the inner value of a successful rail to a new value asynchronously.
        /// </summary>
        public async Task<RailFor<TOut>> MapAsync<TOut>(Func<TValue, Task<TOut>> map) where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return RailFor<TOut>.Fail(rail.Error);

            var value = await map(rail.Value).ConfigureAwait(false);
            return rail.Map(_ => value);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful, returning the original rail.
        /// </summary>
        public async Task<RailFor<TValue>> TapAsync(Action<TValue> action)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Tap(action);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful, returning the original rail.
        /// </summary>
        public async Task<RailFor<TValue>> TapAsync(Func<TValue, Task> action)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess) await action(rail.Value).ConfigureAwait(false);
            return rail;
        }

        /// <summary>
        /// Ensures that the specified predicate is true asynchronously. If false, returns a failed rail with the provided error.
        /// </summary>
        public async Task<RailFor<TValue>> EnsureAsync(Func<TValue, bool> predicate, Error error)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Ensure(predicate, error);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful and the condition is true.
        /// </summary>
        public async Task<RailFor<TValue>> TapIfAsync(bool condition, Func<TValue, Task> action)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess && condition) await action(rail.Value).ConfigureAwait(false);
            return rail;
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful and the predicate evaluates to true.
        /// </summary>
        public async Task<RailFor<TValue>> TapIfAsync(Func<TValue, bool> predicate, Func<TValue, Task> action)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess && predicate(rail.Value)) await action(rail.Value).ConfigureAwait(false);
            return rail;
        }

        /// <summary>
        /// Recovers from a failed rail asynchronously by producing a value.
        /// </summary>
        public async Task<RailFor<TValue>> RecoverAsync(Func<Error, Task<TValue>> recover)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess)
                return rail;

            var value = await recover(rail.Error).ConfigureAwait(false);
            return rail.Recover(_ => value);
        }

        /// <summary>
        /// Replaces a failed rail asynchronously with the rail returned by the fallback function.
        /// </summary>
        public async Task<RailFor<TValue>> RecoverWithAsync(Func<Error, Task<RailFor<TValue>>> fallback)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess)
                return rail;

            var next = await fallback(rail.Error).ConfigureAwait(false);
            return rail.RecoverWith(_ => next);
        }

        /// <summary>
        /// Maps the error of a failed rail to another error asynchronously.
        /// </summary>
        public async Task<RailFor<TValue>> MapErrorAsync(Func<Error, Error> errorMapper)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.MapError(errorMapper);
        }

        /// <summary>
        /// Maps the error of a failed rail to another error asynchronously.
        /// </summary>
        public async Task<RailFor<TValue>> MapErrorAsync(Func<Error, Task<Error>> errorMapper)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess)
                return rail;

            var error = await errorMapper(rail.Error).ConfigureAwait(false);
            return rail.MapError(_ => error);
        }
    }
}
