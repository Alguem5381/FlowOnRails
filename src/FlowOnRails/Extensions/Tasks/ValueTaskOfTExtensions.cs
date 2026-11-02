namespace FlowOnRails.Extensions.Tasks;

using FlowOnRails;

/// <summary>
/// Provides extension methods for <see cref="ValueTask{TResult}"/> to chain operations.
/// </summary>
public static class ValueTaskOfTExtensions
{
    extension<TValue>(ValueTask<RailFor<TValue>> task) where TValue : notnull
    {
        /// <summary>
        /// Matches the rail asynchronously and returns a value based on success or failure.
        /// </summary>
        public async ValueTask<TNext> MatchAsync<TNext>(Func<TValue, TNext> onSuccess, Func<Error, TNext> onFailure)
            where TNext : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Match(onSuccess, onFailure);
        }

        /// <summary>
        /// Matches the rail asynchronously and exposes its value with the semantic success, or its error.
        /// </summary>
        public async ValueTask<TNext> MatchAsync<TNext>(
            Func<TValue, Success, TNext> onSuccess,
            Func<Error, TNext> onFailure)
            where TNext : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Match(onSuccess, onFailure);
        }

        /// <summary>
        /// Binds the rail to another asynchronous operation if successful.
        /// </summary>
        public async ValueTask<RailFor<TOut>> BindAsync<TOut>(Func<TValue, ValueTask<RailFor<TOut>>> bind)
            where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return RailFor<TOut>.Fail(rail.Error!);

            var next = await bind(rail.Value).ConfigureAwait(false);
            return rail.Bind(_ => next);
        }

        /// <summary>
        /// Binds the rail to another asynchronous operation if successful.
        /// </summary>
        public async ValueTask<Rail> BindAsync(Func<TValue, ValueTask<Rail>> bind)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return Rail.Fail(rail.Error!);

            var next = await bind(rail.Value).ConfigureAwait(false);
            return rail.Bind(_ => next);
        }

        /// <summary>
        /// Maps the inner value of a successful rail to a new value asynchronously.
        /// </summary>
        public async ValueTask<RailFor<TOut>> MapAsync<TOut>(Func<TValue, TOut> map) where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Map(map);
        }

        /// <summary>
        /// Maps the inner value of a successful rail to a new value asynchronously.
        /// </summary>
        public async ValueTask<RailFor<TOut>> MapAsync<TOut>(Func<TValue, ValueTask<TOut>> map) where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return RailFor<TOut>.Fail(rail.Error!);

            var value = await map(rail.Value).ConfigureAwait(false);
            return rail.Map(_ => value);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful, returning the original rail.
        /// </summary>
        public async ValueTask<RailFor<TValue>> TapAsync(Action<TValue> action)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Tap(action);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful, returning the original rail.
        /// </summary>
        public async ValueTask<RailFor<TValue>> TapAsync(Func<TValue, ValueTask> action)
        {
            var rail = await task.ConfigureAwait(false);

            if (rail.IsSuccess)
                await action(rail.Value).ConfigureAwait(false);

            return rail;
        }

        /// <summary>
        /// Ensures that the specified predicate is true asynchronously. If false, returns a failed rail with the provided error.
        /// </summary>
        public async ValueTask<RailFor<TValue>> EnsureAsync(Func<TValue, bool> predicate, Error error)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Ensure(predicate, error);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful and the condition is true.
        /// </summary>
        public async ValueTask<RailFor<TValue>> TapIfAsync(bool condition, Func<TValue, ValueTask> action)
        {
            var rail = await task.ConfigureAwait(false);

            if (rail.IsSuccess && condition)
                await action(rail.Value).ConfigureAwait(false);

            return rail;
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful and the predicate evaluates to true.
        /// </summary>
        public async ValueTask<RailFor<TValue>> TapIfAsync(Func<TValue, bool> predicate, Func<TValue, ValueTask> action)
        {
            var rail = await task.ConfigureAwait(false);

            if (rail.IsSuccess && predicate(rail.Value))
                await action(rail.Value).ConfigureAwait(false);

            return rail;
        }

        /// <summary>
        /// Recovers from a failed rail asynchronously by producing a value with neutral semantic success.
        /// </summary>
        public async ValueTask<RailFor<TValue>> RecoverAsync(Func<Error, ValueTask<TValue>> recover)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess)
                return rail;

            var value = await recover(rail.Error!).ConfigureAwait(false);
            return rail.Recover(_ => value);
        }

        /// <summary>
        /// Replaces a failed rail asynchronously with the rail returned by the fallback function.
        /// </summary>
        public async ValueTask<RailFor<TValue>> RecoverWithAsync(Func<Error, ValueTask<RailFor<TValue>>> fallback)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess)
                return rail;

            var next = await fallback(rail.Error!).ConfigureAwait(false);
            return rail.RecoverWith(_ => next);
        }

        /// <summary>
        /// Maps the error of a failed rail to another error asynchronously.
        /// </summary>
        public async ValueTask<RailFor<TValue>> MapErrorAsync(Func<Error, Error> errorMapper)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.MapError(errorMapper);
        }

        /// <summary>
        /// Maps the error of a failed rail to another error asynchronously.
        /// </summary>
        public async ValueTask<RailFor<TValue>> MapErrorAsync(Func<Error, ValueTask<Error>> errorMapper)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess)
                return rail;

            var error = await errorMapper(rail.Error!).ConfigureAwait(false);
            return rail.MapError(_ => error);
        }
    }
}