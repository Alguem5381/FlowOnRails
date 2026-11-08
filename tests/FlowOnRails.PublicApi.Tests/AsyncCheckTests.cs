using System.Threading.Tasks.Sources;

using FlowOnRails.Extensions.Tasks;

namespace FlowOnRails.PublicApi.Tests;

public class AsyncCheckTests
{
    public enum State { Success, Failure, Uninitialized }
    private sealed record DomainError(string Message) : Error;
    private sealed record Payload(string Name);

    public static TheoryData<State, State, bool, bool, bool, bool> Cases
    {
        get
        {
            var cases = new TheoryData<State, State, bool, bool, bool, bool>();
            foreach (var source in Enum.GetValues<State>())
            foreach (var next in Enum.GetValues<State>())
            foreach (var typedSource in new[] { false, true })
            foreach (var typedCallback in new[] { false, true })
            foreach (var valueTask in new[] { false, true })
            foreach (var syncCallback in new[] { false, true })
                cases.Add(source, next, typedSource, typedCallback, valueTask, syncCallback);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CheckAsync_PreservesTheSourceValueOrFirstErrorAndExecutesOnlyOnceWhenApplicable(
        State sourceState, State nextState, bool typedSource, bool typedCallback, bool valueTask, bool syncCallback)
    {
        var originalValue = new Payload("original");
        var sourceError = new DomainError("source");
        var nextError = new DomainError("checked");
        var calls = 0;
        var plain = sourceState switch
        {
            State.Success => Rail.Ok(),
            State.Failure => Rail.Fail(sourceError),
            _ => default
        };
        var valued = sourceState switch
        {
            State.Success => Rail.Ok(originalValue),
            State.Failure => Rail.Fail<Payload>(sourceError),
            _ => default
        };

        Rail CheckVoid()
        {
            calls++;
            return nextState switch
            {
                State.Success => Rail.Ok(),
                State.Failure => Rail.Fail(nextError),
                _ => default
            };
        }
        RailFor<int> CheckValue()
        {
            calls++;
            return nextState switch
            {
                State.Success => Rail.Ok(42),
                State.Failure => Rail.Fail<int>(nextError),
                _ => default
            };
        }
        async Task<object> Execute()
        {
            if (!valueTask)
            {
                if (!typedSource)
                {
                    if (!typedCallback)
                    {
                        if (syncCallback) return await Task.FromResult(plain).CheckAsync(CheckVoid);
                        return await Task.FromResult(plain).CheckAsync(async () => { await Task.Yield(); return CheckVoid(); });
                    }
                    if (typedCallback)
                    {
                        if (syncCallback) return await Task.FromResult(plain).CheckAsync(CheckValue);
                        return await Task.FromResult(plain).CheckAsync(async () => { await Task.Yield(); return CheckValue(); });
                    }
                }
                if (typedSource)
                {
                    if (!typedCallback)
                    {
                        if (syncCallback) return await Task.FromResult(valued).CheckAsync(value => { Assert.Same(originalValue, value); return CheckVoid(); });
                        return await Task.FromResult(valued).CheckAsync(async value => { Assert.Same(originalValue, value); await Task.Yield(); return CheckVoid(); });
                    }
                    if (typedCallback)
                    {
                        if (syncCallback) return await Task.FromResult(valued).CheckAsync(value => { Assert.Same(originalValue, value); return CheckValue(); });
                        return await Task.FromResult(valued).CheckAsync(async value => { Assert.Same(originalValue, value); await Task.Yield(); return CheckValue(); });
                    }
                }
            }
            else
            {
                if (!typedSource)
                {
                    if (!typedCallback)
                    {
                        if (syncCallback) return await ValueTask.FromResult(plain).CheckAsync(CheckVoid);
                        return await ValueTask.FromResult(plain).CheckAsync(async () => { await Task.Yield(); return CheckVoid(); });
                    }
                    if (typedCallback)
                    {
                        if (syncCallback) return await ValueTask.FromResult(plain).CheckAsync(CheckValue);
                        return await ValueTask.FromResult(plain).CheckAsync(async () => { await Task.Yield(); return CheckValue(); });
                    }
                }
                if (typedSource)
                {
                    if (!typedCallback)
                    {
                        if (syncCallback) return await ValueTask.FromResult(valued).CheckAsync(value => { Assert.Same(originalValue, value); return CheckVoid(); });
                        return await ValueTask.FromResult(valued).CheckAsync(async value => { Assert.Same(originalValue, value); await Task.Yield(); return CheckVoid(); });
                    }
                    if (typedCallback)
                    {
                        if (syncCallback) return await ValueTask.FromResult(valued).CheckAsync(value => { Assert.Same(originalValue, value); return CheckValue(); });
                        return await ValueTask.FromResult(valued).CheckAsync(async value => { Assert.Same(originalValue, value); await Task.Yield(); return CheckValue(); });
                    }
                }
            }
            throw new InvalidOperationException();
        }

        if (sourceState == State.Uninitialized ||
            (sourceState == State.Success && nextState == State.Uninitialized))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Execute());
            Assert.Equal(sourceState == State.Uninitialized ? 0 : 1, calls);
            return;
        }

        var result = await Execute();
        Assert.Equal(sourceState == State.Success ? 1 : 0, calls);
        var expectedError = sourceState == State.Failure ? sourceError :
            nextState == State.Failure ? nextError : null;
        if (typedSource)
        {
            var observed = Assert.IsType<RailFor<Payload>>(result);
            Assert.Equal(expectedError is null, observed.TryUnwrap(out var value, out var error));
            Assert.Same(expectedError, error);
            Assert.Same(expectedError is null ? originalValue : null, value);
            if (expectedError is null)
                Assert.Equal(valued, observed);
        }
        else
        {
            var observed = Assert.IsType<Rail>(result);
            Assert.Equal(expectedError is null, observed.TryUnwrap(out var error));
            Assert.Same(expectedError, error);
        }
    }


    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CheckAsync_WaitsForBothSourceAndCallback(bool valueTask, bool typedCallback)
    {
        var source = new TaskCompletionSource<RailFor<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new TaskCompletionSource<RailFor<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var pending = CheckUsing(valueTask, typedCallback, source.Task, value =>
        {
            Assert.Equal(1, value);
            Interlocked.Increment(ref calls);
            started.SetResult();
            return callback.Task;
        });
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref calls));
        source.SetResult(Rail.Ok(1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(pending.IsCompleted);
        callback.SetResult(Rail.Ok(42));
        Assert.Equal(Rail.Ok(1), await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CheckAsync_PropagatesFaultsAndCancellation(bool valueTask, bool typedCallback)
    {
        var exception = new InvalidOperationException("expected");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (var fromCallback in new[] { false, true })
        {
            var faulted = Task.FromException<RailFor<int>>(exception);
            var source = fromCallback ? Task.FromResult(Rail.Ok(1)) : faulted;
            var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CheckUsing(valueTask, typedCallback, source, _ => faulted));
            Assert.Same(exception, observed);

            var canceled = Task.FromCanceled<RailFor<int>>(cancellation.Token);
            source = fromCallback ? Task.FromResult(Rail.Ok(1)) : canceled;
            var pending = CheckUsing(valueTask, typedCallback, source, _ => canceled);
            var canceledException = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.True(pending.IsCanceled);
            Assert.Equal(cancellation.Token, canceledException.CancellationToken);
        }
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CheckUsing(valueTask, typedCallback, Task.FromResult(Rail.Ok(1)), _ => throw exception));
        Assert.Same(exception, thrown);
    }

    [Fact]
    public async Task CheckAsync_ConsumesPendingValueTaskSourcesAndCallbacksExactlyOnce()
    {
        var source = new SingleUseSource();
        var callback = new SingleUseSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ValueTask<RailFor<int>> pending = source.Awaitable.CheckAsync(value =>
        {
            Assert.Equal(1, value);
            started.SetResult();
            return callback.Awaitable;
        });
        Assert.False(pending.IsCompleted);
        source.Complete(Rail.Ok(1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(pending.IsCompleted);
        callback.Complete(Rail.Ok(42));
        Assert.Equal(Rail.Ok(1), await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, source.GetResultCalls);
        Assert.Equal(1, callback.GetResultCalls);
    }

    private static Task<RailFor<int>> CheckUsing(bool valueTask, bool typedCallback,
        Task<RailFor<int>> source, Func<int, Task<RailFor<int>>> callback)
    {
        async Task<Rail> CheckVoid(int value) => await callback(value);
        if (!valueTask)
            return typedCallback ? source.CheckAsync(callback) : source.CheckAsync(CheckVoid);

        return typedCallback
            ? new ValueTask<RailFor<int>>(source)
                .CheckAsync(value => new ValueTask<RailFor<int>>(callback(value))).AsTask()
            : new ValueTask<RailFor<int>>(source)
                .CheckAsync(value => new ValueTask<Rail>(CheckVoid(value))).AsTask();
    }

    private sealed class SingleUseSource : IValueTaskSource<RailFor<int>>
    {
        private ManualResetValueTaskSourceCore<RailFor<int>> _core = new() { RunContinuationsAsynchronously = true };
        private int _getResultCalls;
        public int GetResultCalls => Volatile.Read(ref _getResultCalls);
        public ValueTask<RailFor<int>> Awaitable => new(this, _core.Version);
        public void Complete(RailFor<int> result) => _core.SetResult(result);

        public RailFor<int> GetResult(short token)
        {
            if (Interlocked.Increment(ref _getResultCalls) != 1)
                throw new InvalidOperationException("ValueTask was consumed more than once.");
            return _core.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token) => _core.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state, short token,
            ValueTaskSourceOnCompletedFlags flags) => _core.OnCompleted(continuation, state, token, flags);
    }
}
