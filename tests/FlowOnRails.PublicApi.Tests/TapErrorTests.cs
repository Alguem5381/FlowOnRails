using FlowOnRails.Extensions.Tasks;

namespace FlowOnRails.PublicApi.Tests;

public class TapErrorTests
{
    public enum State { Success, Failure, Uninitialized }
    private sealed record DomainError(string Message) : Error;
    private sealed record Payload(string Name);

    public static TheoryData<State, bool, int> Cases
    {
        get
        {
            var cases = new TheoryData<State, bool, int>();
            foreach (var state in Enum.GetValues<State>())
            foreach (var typed in new[] { false, true })
            for (var family = 0; family < 5; family++)
                cases.Add(state, typed, family);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TapError_ObservesOnlyFailuresAndPreservesTheSource(State state, bool typed, int family)
    {
        var error = new DomainError("original");
        var payload = new Payload("original");
        var calls = 0;
        var plain = state switch { State.Success => Rail.Ok(), State.Failure => Rail.Fail(error), _ => default };
        var valued = state switch { State.Success => Rail.Ok(payload), State.Failure => Rail.Fail<Payload>(error), _ => default };
        void Observe(Error observed) { Assert.Same(error, observed); calls++; }
        async Task ObserveTask(Error observed) { await Task.Yield(); Observe(observed); }
        async ValueTask ObserveValueTask(Error observed) { await Task.Yield(); Observe(observed); }
        async Task<object> Execute()
        {
            if (typed)
                return family switch
                {
                    0 => valued.TapError(Observe),
                    1 => await Task.FromResult(valued).TapErrorAsync(Observe),
                    2 => await Task.FromResult(valued).TapErrorAsync(ObserveTask),
                    3 => await ValueTask.FromResult(valued).TapErrorAsync(Observe),
                    _ => await ValueTask.FromResult(valued).TapErrorAsync(ObserveValueTask)
                };
            return family switch
            {
                0 => plain.TapError(Observe),
                1 => await Task.FromResult(plain).TapErrorAsync(Observe),
                2 => await Task.FromResult(plain).TapErrorAsync(ObserveTask),
                3 => await ValueTask.FromResult(plain).TapErrorAsync(Observe),
                _ => await ValueTask.FromResult(plain).TapErrorAsync(ObserveValueTask)
            };
        }
        if (state == State.Uninitialized)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(Execute);
            Assert.Equal(0, calls);
            return;
        }
        var result = await Execute();
        Assert.Equal(state == State.Failure ? 1 : 0, calls);
        if (typed)
        {
            var observed = Assert.IsType<RailFor<Payload>>(result);
            Assert.Equal(valued, observed);
            Assert.Equal(state == State.Success, observed.TryUnwrap(out var value, out var failure));
            Assert.Same(state == State.Success ? payload : null, value);
            Assert.Same(state == State.Failure ? error : null, failure);
        }
        else
        {
            var observed = Assert.IsType<Rail>(result);
            Assert.Equal(plain, observed);
            Assert.Equal(state == State.Success, observed.TryUnwrap(out var failure));
            Assert.Same(state == State.Failure ? error : null, failure);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TapErrorAsync_WaitsForSourceAndObserver(bool typed, bool valueTask)
    {
        var error = new DomainError("original");
        var source = new TaskCompletionSource<RailFor<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Observe(Error observed)
        {
            Assert.Same(error, observed);
            started.SetResult();
            await gate.Task;
        }
        async Task Execute()
        {
            if (typed)
            {
                var result = valueTask
                    ? await new ValueTask<RailFor<int>>(source.Task).TapErrorAsync(e => new ValueTask(Observe(e)))
                    : await source.Task.TapErrorAsync(Observe);
                Assert.False(result.TryUnwrap(out _, out var failure));
                Assert.Same(error, failure);
            }
            else
            {
                async Task<Rail> Plain() => await source.Task;
                var result = valueTask
                    ? await new ValueTask<Rail>(Plain()).TapErrorAsync(e => new ValueTask(Observe(e)))
                    : await Plain().TapErrorAsync(Observe);
                Assert.False(result.TryUnwrap(out var failure));
                Assert.Same(error, failure);
            }
        }
        var pending = Execute();
        Assert.False(pending.IsCompleted);
        Assert.False(started.Task.IsCompleted);
        source.SetResult(Rail.Fail<int>(error));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(pending.IsCompleted);
        gate.SetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapError_PropagatesObserverExceptionsAndCancellation(bool typed)
    {
        var error = new DomainError("original");
        var exception = new InvalidOperationException("observer");
        void Throw(Error _) => throw exception;
        var observed = Assert.Throws<InvalidOperationException>(() =>
        {
            if (typed) Rail.Fail<int>(error).TapError(Throw);
            else Rail.Fail(error).TapError(Throw);
        });
        Assert.Same(exception, observed);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (var valueTask in new[] { false, true })
        {
            Task ObserveFailure(Error _) => Task.FromException(exception);
            Task ObserveCancellation(Error _) => Task.FromCanceled(cancellation.Token);
            async Task Execute(Func<Error, Task> observer)
            {
                if (typed)
                {
                    if (valueTask) await ValueTask.FromResult(Rail.Fail<int>(error)).TapErrorAsync(e => new ValueTask(observer(e)));
                    else await Rail.FailAsync<int>(error).TapErrorAsync(observer);
                }
                else
                {
                    if (valueTask) await ValueTask.FromResult(Rail.Fail(error)).TapErrorAsync(e => new ValueTask(observer(e)));
                    else await Rail.FailAsync(error).TapErrorAsync(observer);
                }
            }
            Assert.Same(exception, await Assert.ThrowsAsync<InvalidOperationException>(() => Execute(ObserveFailure)));
            var canceled = Execute(ObserveCancellation);
            var observedCancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceled);
            Assert.True(canceled.IsCanceled);
            Assert.Equal(cancellation.Token, observedCancellation.CancellationToken);
        }
    }
}
