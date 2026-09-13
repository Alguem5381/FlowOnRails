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
        /// Matches the rail asynchronously and returns a value based on success or failure.
        /// </summary>
        public async Task<TNext> MatchAsync<TNext>(Func<TValue, TNext> onSuccess, Func<Error, TNext> onFailure)
            where TNext : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.IsSuccess ? onSuccess(rail.Value) : onFailure(rail.Error!);
        }

        /// <summary>
        /// Binds the rail to another asynchronous operation if successful.
        /// </summary>
        public async Task<RailFor<TOut>> BindAsync<TOut>(Func<TValue, Task<RailFor<TOut>>> bind)
            where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.IsSuccess
                ? await bind(rail.Value).ConfigureAwait(false)
                : RailFor<TOut>.Fail(rail.Error!);
        }

        /// <summary>
        /// Binds the rail to another asynchronous operation if successful.
        /// </summary>
        public async Task<RailFor<TOut>> BindAsync<TOut>(Func<TValue, ValueTask<RailFor<TOut>>> bind)
            where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.IsSuccess
                ? await bind(rail.Value).ConfigureAwait(false)
                : RailFor<TOut>.Fail(rail.Error!);
        }

        /// <summary>
        /// Binds the rail to another asynchronous operation if successful.
        /// </summary>
        public async Task<Rail> BindAsync(Func<TValue, Task<Rail>> bind)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.IsSuccess
                ? await bind(rail.Value).ConfigureAwait(false)
                : Rail.Fail(rail.Error!);
        }

        /// <summary>
        /// Binds the rail to another asynchronous operation if successful.
        /// </summary>
        public async Task<Rail> BindAsync(Func<TValue, ValueTask<Rail>> bind)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.IsSuccess
                ? await bind(rail.Value).ConfigureAwait(false)
                : Rail.Fail(rail.Error!);
        }

        /// <summary>
        /// Maps the inner value of a successful rail to a new value asynchronously.
        /// </summary>
        public async Task<RailFor<TOut>> MapAsync<TOut>(Func<TValue, TOut> map) where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.IsSuccess
                ? RailFor<TOut>.Ok(map(rail.Value))
                : RailFor<TOut>.Fail(rail.Error!);
        }

        /// <summary>
        /// Maps the inner value of a successful rail to a new value asynchronously.
        /// </summary>
        public async Task<RailFor<TOut>> MapAsync<TOut>(Func<TValue, Task<TOut>> map) where TOut : notnull
        {
            var rail = await task.ConfigureAwait(false);
            return rail.IsSuccess
                ? RailFor<TOut>.Ok(await map(rail.Value).ConfigureAwait(false))
                : RailFor<TOut>.Fail(rail.Error!);
        }

        /// <summary>
        /// Executes an action asynchronously if the rail is successful, returning the original rail.
        /// </summary>
        public async Task<RailFor<TValue>> TapAsync(Action<TValue> action)
        {
            var rail = await task.ConfigureAwait(false);
            if (rail.IsSuccess) action(rail.Value);
            return rail;
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
            return rail.IsFailure
                ? rail
                : predicate(rail.Value)
                    ? rail
                    : RailFor<TValue>.Fail(error);
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
        /// Recovers from a failed rail asynchronously by invoking the fallback function.
        /// </summary>
        public async Task<RailFor<TValue>> RecoverAsync(Func<Error, Task<RailFor<TValue>>> fallback)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.IsSuccess
                ? rail
                : await fallback(rail.Error!).ConfigureAwait(false);
        }

        /// <summary>
        /// Maps the error of a failed rail to another error asynchronously.
        /// </summary>
        public async Task<RailFor<TValue>> MapErrorAsync(Func<Error, Error> errorMapper)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.IsSuccess
                ? rail
                : RailFor<TValue>.Fail(errorMapper(rail.Error!));
        }

        /// <summary>
        /// Maps the error of a failed rail to another error asynchronously.
        /// </summary>
        public async Task<RailFor<TValue>> MapErrorAsync(Func<Error, Task<Error>> errorMapper)
        {
            var rail = await task.ConfigureAwait(false);
            return rail.IsSuccess
                ? rail
                : RailFor<TValue>.Fail(await errorMapper(rail.Error!).ConfigureAwait(false));
        }
    }
}
