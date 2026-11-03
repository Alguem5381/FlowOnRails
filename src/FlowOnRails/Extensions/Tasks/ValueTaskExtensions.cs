namespace FlowOnRails.Extensions.Tasks;

using FlowOnRails;

/// <summary>
/// Provides extension methods for <see cref="ValueTask{Rail}"/> to chain operations.
/// </summary>
public static class ValueTaskExtensions
{
    extension(ValueTask<Rail> task)
    {
        /// <summary>
        /// Checks another operation on success, preserving the source value, if any, and propagating failure.
        /// </summary>
        public async ValueTask<Rail> CheckAsync(Func<Rail> check)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Check(check);
        }

        /// <summary>
        /// Checks another operation on success, preserving the source value, if any, and propagating failure.
        /// </summary>
        public async ValueTask<Rail> CheckAsync(Func<ValueTask<Rail>> check)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure) return rail;
            var next = await check().ConfigureAwait(false);
            return rail.Check(() => next);
        }

        /// <summary>
        /// Checks another operation on success, preserving the source value, if any, and propagating failure.
        /// </summary>
        public async ValueTask<Rail> CheckAsync<TChecked>(Func<RailFor<TChecked>> check)
            where TChecked : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Check(check);
        }

        /// <summary>
        /// Checks another operation on success, preserving the source value, if any, and propagating failure.
        /// </summary>
        public async ValueTask<Rail> CheckAsync<TChecked>(Func<ValueTask<RailFor<TChecked>>> check)
            where TChecked : notnull
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure) return rail;
            var next = await check().ConfigureAwait(false);
            return rail.Check(() => next);
        }

        /// <summary>
        /// Matches the rail asynchronously and returns a value based on success or failure.
        /// </summary>
        public async ValueTask<TNext> MatchAsync<TNext>(Func<TNext> onSuccess, Func<Error, TNext> onFailure)
            where TNext : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Match(onSuccess, onFailure);
        }

            /// <summary>
        /// Binds the rail to another asynchronous operation if successful.
        /// </summary>
        public async ValueTask<Rail> BindAsync(Func<ValueTask<Rail>> bind)
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
        public async ValueTask<RailFor<TOut>> BindAsync<TOut>(Func<ValueTask<RailFor<TOut>>> bind)
            where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return RailFor<TOut>.Fail(rail.Error);

            var next = await bind().ConfigureAwait(false);
            return rail.Bind(() => next);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful, returning the original rail.
        /// </summary>
        public async ValueTask<Rail> TapAsync(Action action)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Tap(action);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful, returning the original rail.
        /// </summary>
        public async ValueTask<Rail> TapAsync(Func<ValueTask> action)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess) await action().ConfigureAwait(false);
            return rail;
        }

        /// <summary>
        /// Ensures that the specified predicate is true asynchronously. If false, returns a failed rail with the provided error.
        /// </summary>
        public async ValueTask<Rail> EnsureAsync(Func<bool> predicate, Error error)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Ensure(predicate, error);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful and the condition is true.
        /// </summary>
        public async ValueTask<Rail> TapIfAsync(bool condition, Func<ValueTask> action)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess && condition) await action().ConfigureAwait(false);
            return rail;
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful and the predicate evaluates to true.
        /// </summary>
        public async ValueTask<Rail> TapIfAsync(Func<bool> predicate, Func<ValueTask> action)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess && predicate()) await action().ConfigureAwait(false);
            return rail;
        }

            /// <summary>
        /// Replaces a failed rail asynchronously with the rail returned by the fallback function.
        /// </summary>
        public async ValueTask<Rail> RecoverWithAsync(Func<Error, ValueTask<Rail>> fallback)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess)
                return rail;

            var next = await fallback(rail.Error).ConfigureAwait(false);
            return rail.RecoverWith(_ => next);
        }

        /// <summary>
        /// Maps a successful rail to a new value asynchronously.
        /// </summary>
        public async ValueTask<RailFor<TOut>> MapAsync<TOut>(Func<TOut> map) where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.Map(map);
        }

        /// <summary>
        /// Maps a successful rail to a new value asynchronously.
        /// </summary>
        public async ValueTask<RailFor<TOut>> MapAsync<TOut>(Func<ValueTask<TOut>> map) where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsFailure)
                return RailFor<TOut>.Fail(rail.Error);

            var value = await map().ConfigureAwait(false);
            return rail.Map(() => value);
        }

        /// <summary>
        /// Maps the error of a failed rail to another error asynchronously.
        /// </summary>
        public async ValueTask<Rail> MapErrorAsync(Func<Error, Error> errorMapper)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.MapError(errorMapper);
        }

        /// <summary>
        /// Maps the error of a failed rail to another error asynchronously.
        /// </summary>
        public async ValueTask<Rail> MapErrorAsync(Func<Error, ValueTask<Error>> errorMapper)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess)
                return rail;

            var error = await errorMapper(rail.Error).ConfigureAwait(false);
            return rail.MapError(_ => error);
        }
    }
}
