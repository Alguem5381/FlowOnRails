using System.Threading.Tasks.Sources;

using FlowOnRails.Extensions.Tasks;

namespace FlowOnRails.PublicApi.Tests;

public class AsyncCompositionTests
{
    private sealed record DomainError(string Message) : Error;

    [Fact]
    public async Task AsyncLambdas_CompileWithoutCastsForTaskAndValueTask()
    {
        var taskResult = await Rail.OkAsync(1).BindAsync(async value =>
        {
            await Task.Yield();
            return Rail.Ok(value + 1);
        });
        var valueTaskResult = await ValueTask.FromResult(Rail.Ok(1)).BindAsync(async value =>
        {
            await Task.Yield();
            return Rail.Ok(value + 1);
        });
        var bridgeResult = await Rail.OkAsync(1).BindValueTaskAsync(async value =>
        {
            await Task.Yield();
            return Rail.Ok(value + 1);
        });
        var untypedTask = await Rail.OkAsync(1).BindAsync(async _ =>
        {
            await Task.Yield();
            return Rail.Ok();
        });
        var untypedBridge = await Rail.OkAsync(1).BindValueTaskAsync(async _ =>
        {
            await Task.Yield();
            return Rail.Ok();
        });

        Assert.Equal(Rail.Ok(2), taskResult);
        Assert.Equal(Rail.Ok(2), valueTaskResult);
        Assert.Equal(Rail.Ok(2), bridgeResult);
        Assert.Equal(Rail.Ok(), untypedTask);
        Assert.Equal(Rail.Ok(), untypedBridge);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Bind_WaitsForSourceAndCallbackAndReturnsTheCallbackValue(int family)
    {
        var source = new TaskCompletionSource<RailFor<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new TaskCompletionSource<RailFor<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var pending = BindUsing(family, source.Task, value =>
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
        callback.SetResult(Rail.Ok(2));

        Assert.Equal(Rail.Ok(2), await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Bind_FailureSkipsCallbackAndCallbackFailurePreservesError(int family)
    {
        var error = new DomainError("failed");
        var calls = 0;
        var result = await BindUsing(family, Task.FromResult(Rail.Fail<int>(error)), value =>
        {
            calls++;
            return Task.FromResult(Rail.Ok(value));
        });
        Assert.Equal(0, calls);
        Assert.False(result.TryUnwrap(out _, out var failure));
        Assert.Same(error, failure);

        var failedNext = await BindUsing(family,
            Task.FromResult(Rail.Ok(1)),
            _ => Task.FromResult(Rail.Fail<int>(error)));
        Assert.False(failedNext.TryUnwrap(out _, out var nextFailure));
        Assert.Same(error, nextFailure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Bind_RejectsUninitializedCallbackResults(int family)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BindUsing(family, Task.FromResult(Rail.Ok(1)), _ => Task.FromResult(default(RailFor<int>))));
    }

    [Fact]
    public async Task Recovery_RejectsUninitializedCallbackResultsAcrossBothAsyncFamilies()
    {
        var error = new DomainError("failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Rail.FailAsync(error).RecoverWithAsync(_ => Task.FromResult(default(Rail))));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Rail.FailAsync<int>(error).RecoverWithAsync(_ => Task.FromResult(default(RailFor<int>))));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ValueTask.FromResult(Rail.Fail(error)).RecoverWithAsync(_ => ValueTask.FromResult(default(Rail))).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ValueTask.FromResult(Rail.Fail<int>(error)).RecoverWithAsync(_ => ValueTask.FromResult(default(RailFor<int>))).AsTask());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task Bind_PropagatesTaskExceptions(int family, bool fromCallback)
    {
        var exception = new InvalidOperationException("expected");
        var failed = Task.FromException<RailFor<int>>(exception);
        var source = fromCallback ? Task.FromResult(Rail.Ok(1)) : failed;
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BindUsing(family, source, _ => failed));
        Assert.Same(exception, observed);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public async Task Bind_PropagatesCancellationWithoutConvertingItToDomainFailure(int family, bool fromCallback)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = Task.FromCanceled<RailFor<int>>(cancellation.Token);
        var source = fromCallback ? Task.FromResult(Rail.Ok(1)) : canceled;
        var pending = BindUsing(family, source, _ => canceled);
        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(pending.IsCanceled);
        Assert.Equal(cancellation.Token, observed.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValueTaskTapChain_WaitsForSecondOperation(bool typed)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;

        async ValueTask Save()
        {
            await gate.Task;
            completed = true;
        }

        if (typed)
        {
            ValueTask<RailFor<int>> pending = ValueTask.FromResult(Rail.Ok(1))
                .TapAsync(_ => ValueTask.CompletedTask)
                .TapAsync(_ => Save());
            Assert.False(pending.IsCompleted);
            Assert.False(completed);
            gate.SetResult();
            Assert.Equal(Rail.Ok(1), await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        }
        else
        {
            ValueTask<Rail> pending = ValueTask.FromResult(Rail.Ok())
                .TapAsync(() => ValueTask.CompletedTask)
                .TapAsync(() => Save());
            Assert.False(pending.IsCompleted);
            Assert.False(completed);
            gate.SetResult();
            Assert.Equal(Rail.Ok(), await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        }

        Assert.True(completed);
    }

    [Fact]
    public async Task ValueTaskMapChain_ProducesTheAwaitedValue()
    {
        var callback = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        ValueTask<RailFor<int>> pending = ValueTask.FromResult(Rail.Ok(1))
            .MapAsync(value => value + 1)
            .MapAsync(_ => new ValueTask<int>(callback.Task));
        Assert.False(pending.IsCompleted);
        callback.SetResult(3);
        Assert.Equal(Rail.Ok(3), await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task ValueTaskWrappers_PreserveTheirFamilyAcrossRecoveryAndValueTransitions()
    {
        var error = new DomainError("original");
        var mapped = new DomainError("mapped");
        ValueTask<RailFor<int>> pipeline = ValueTask.FromResult(Rail.Fail(error))
            .MapErrorAsync(_ => ValueTask.FromResult<Error>(mapped))
            .RecoverWithAsync(e =>
            {
                Assert.Same(mapped, e);
                return ValueTask.FromResult(Rail.Ok());
            })
            .EnsureAsync(() => true, error)
            .TapAsync(() => { })
            .TapIfAsync(true, () => ValueTask.CompletedTask)
            .TapIfAsync(() => true, () => ValueTask.CompletedTask)
            .RecoverWithAsync(_ => throw new InvalidOperationException("Must be skipped."))
            .BindAsync(() => ValueTask.FromResult(Rail.Ok(1)))
            .MapAsync(value => value + 1)
            .EnsureAsync(value => value == 2, error)
            .TapAsync(_ => { })
            .TapIfAsync(true, _ => ValueTask.CompletedTask)
            .TapIfAsync(value => value == 2, _ => ValueTask.CompletedTask)
            .MapErrorAsync(e => e)
            .RecoverAsync(_ => throw new InvalidOperationException("Must be skipped."))
            .RecoverWithAsync(_ => throw new InvalidOperationException("Must be skipped."));
        var result = await pipeline;
        Assert.Equal(Rail.Ok(2), result);

        ValueTask<Rail> untyped = ValueTask.FromResult(result)
            .BindAsync(_ => ValueTask.FromResult(Rail.Ok()))
            .BindAsync(() => ValueTask.FromResult(Rail.Ok()));
        Assert.Equal(Rail.Ok(), await untyped);

        ValueTask<RailFor<int>> recovered = ValueTask.FromResult(Rail.Fail<int>(error))
            .MapErrorAsync(_ => ValueTask.FromResult<Error>(mapped))
            .RecoverWithAsync(e =>
            {
                Assert.Same(mapped, e);
                return ValueTask.FromResult(Rail.Ok(42));
            });
        Assert.Equal(Rail.Ok(42), await recovered);
    }

    [Fact]
    public async Task ValueTaskBind_ConsumesEachPendingSourceExactlyOnce()
    {
        var source = new SingleUseSource();
        var callback = new SingleUseSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = source.Awaitable.BindAsync(value =>
        {
            Assert.Equal(1, value);
            started.SetResult();
            return callback.Awaitable;
        });
        Assert.False(pending.IsCompleted);
        source.Complete(Rail.Ok(1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(pending.IsCompleted);
        callback.Complete(Rail.Ok(2));
        Assert.Equal(Rail.Ok(2), await pending.AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, source.GetResultCalls);
        Assert.Equal(1, callback.GetResultCalls);
    }

    private static Task<RailFor<int>> BindUsing(int family, Task<RailFor<int>> source,
        Func<int, Task<RailFor<int>>> callback) =>
        family switch
        {
            0 => source.BindAsync(callback),
            1 => new ValueTask<RailFor<int>>(source)
                .BindAsync(value => new ValueTask<RailFor<int>>(callback(value))).AsTask(),
            2 => source.BindValueTaskAsync(value => new ValueTask<RailFor<int>>(callback(value))),
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };

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
