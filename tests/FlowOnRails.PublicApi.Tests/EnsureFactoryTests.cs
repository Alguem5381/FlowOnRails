using FlowOnRails.Extensions.Tasks;

namespace FlowOnRails.PublicApi.Tests;

public class EnsureFactoryTests
{
    public enum State { Success, Failure, Uninitialized }
    private sealed record DomainError(string Message) : Error;
    private sealed record Payload(string Id);

    public static TheoryData<State, bool, bool, int> Cases
    {
        get
        {
            var cases = new TheoryData<State, bool, bool, int>();
            foreach (var state in Enum.GetValues<State>())
            foreach (var typed in new[] { false, true })
            foreach (var predicateResult in new[] { false, true })
            for (var family = 0; family < 3; family++)
                cases.Add(state, typed, predicateResult, family);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Ensure_CreatesContextualErrorOnlyWhenPredicateFails(
        State state, bool typed, bool predicateResult, int family)
    {
        var payload = new Payload("user-42");
        var originalError = new DomainError("original");
        DomainError? createdError = null;
        var calls = new List<string>();
        var plain = state switch { State.Success => Rail.Ok(), State.Failure => Rail.Fail(originalError), _ => default };
        var valued = state switch { State.Success => Rail.Ok(payload), State.Failure => Rail.Fail<Payload>(originalError), _ => default };
        bool Predicate() { calls.Add("predicate"); return predicateResult; }
        Error CreateError() { calls.Add("factory"); return createdError = new DomainError("rejected"); }
        bool TypedPredicate(Payload value) { Assert.Same(payload, value); return Predicate(); }
        Error CreateTypedError(Payload value)
        {
            Assert.Same(payload, value);
            calls.Add("factory");
            return createdError = new DomainError($"Inactive: {value.Id}");
        }
        async Task<object> Execute()
        {
            if (typed)
                return family switch
                {
                    0 => valued.Ensure(TypedPredicate, CreateTypedError),
                    1 => await Task.FromResult(valued).EnsureAsync(TypedPredicate, CreateTypedError),
                    _ => await ValueTask.FromResult(valued).EnsureAsync(TypedPredicate, CreateTypedError)
                };
            return family switch
            {
                0 => plain.Ensure(Predicate, CreateError),
                1 => await Task.FromResult(plain).EnsureAsync(Predicate, CreateError),
                _ => await ValueTask.FromResult(plain).EnsureAsync(Predicate, CreateError)
            };
        }
        if (state == State.Uninitialized)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(Execute);
            Assert.Empty(calls);
            return;
        }
        var result = await Execute();
        Assert.Equal(state == State.Failure ? Array.Empty<string>() :
            predicateResult ? new[] { "predicate" } : new[] { "predicate", "factory" }, calls);
        var success = state == State.Success && predicateResult;
        var expectedError = state == State.Failure ? originalError : createdError;
        if (typed)
        {
            var observed = Assert.IsType<RailFor<Payload>>(result);
            Assert.Equal(success, observed.TryUnwrap(out var value, out var error));
            Assert.Same(success ? payload : null, value);
            Assert.Same(expectedError, error);
            if (createdError is not null) Assert.Equal("Inactive: user-42", createdError.Message);
        }
        else
        {
            var observed = Assert.IsType<Rail>(result);
            Assert.Equal(success, observed.TryUnwrap(out var error));
            Assert.Same(expectedError, error);
        }
        Assert.Equal(state == State.Success && !predicateResult, createdError is not null);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task Ensure_PropagatesCallbackExceptionsAndRejectsNullProducedErrors(bool typed, int family)
    {
        var exception = new InvalidOperationException("callback");
        async Task Execute(Func<bool> predicate, Func<Error> factory)
        {
            if (typed)
            {
                var source = Rail.Ok(42);
                switch (family)
                {
                    case 0: source.Ensure(_ => predicate(), _ => factory()); break;
                    case 1: await Task.FromResult(source).EnsureAsync(_ => predicate(), _ => factory()); break;
                    default: await ValueTask.FromResult(source).EnsureAsync(_ => predicate(), _ => factory()); break;
                }
            }
            else
            {
                var source = Rail.Ok();
                switch (family)
                {
                    case 0: source.Ensure(predicate, factory); break;
                    case 1: await Task.FromResult(source).EnsureAsync(predicate, factory); break;
                    default: await ValueTask.FromResult(source).EnsureAsync(predicate, factory); break;
                }
            }
        }
        Assert.Same(exception, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Execute(() => throw exception, () => throw new Exception("Must be skipped"))));
        Assert.Same(exception, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Execute(() => false, () => throw exception)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Execute(() => false, () => null!));
        await Execute(() => true, () => throw new Exception("Must be skipped"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureAsync_WaitsForSourceBeforeInspectingValue(bool valueTask)
    {
        var source = new TaskCompletionSource<RailFor<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        bool Predicate(int value) { Assert.Equal(42, value); calls.Add("predicate"); return false; }
        Error Factory(int value) { calls.Add("factory"); return new DomainError($"Invalid: {value}"); }
        var pending = valueTask
            ? new ValueTask<RailFor<int>>(source.Task).EnsureAsync(Predicate, Factory).AsTask()
            : source.Task.EnsureAsync(Predicate, Factory);
        Assert.False(pending.IsCompleted);
        Assert.Empty(calls);
        source.SetResult(Rail.Ok(42));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(result.TryUnwrap(out _, out var error));
        Assert.Equal("Invalid: 42", Assert.IsType<DomainError>(error).Message);
        Assert.Equal(new[] { "predicate", "factory" }, calls);
    }
    [Fact]
    public async Task ContextualEnsureAndTapError_ComposeWithOrdinaryAsyncLambdas()
    {
        var observed = new List<string>();
        Task<RailFor<int>> task = Rail.OkAsync(42)
            .EnsureAsync(value => value < 0, value => new DomainError($"Invalid: {value}"))
            .TapErrorAsync(async error =>
            {
                await Task.Yield();
                observed.Add(Assert.IsType<DomainError>(error).Message);
            });
        ValueTask<RailFor<int>> valueTask = ValueTask.FromResult(Rail.Ok(43))
            .EnsureAsync(value => value < 0, value => new DomainError($"Invalid: {value}"))
            .TapErrorAsync(async error =>
            {
                await Task.Yield();
                observed.Add(Assert.IsType<DomainError>(error).Message);
            });
        Assert.True((await task).IsFailure);
        Assert.True((await valueTask).IsFailure);
        Assert.Equal(new[] { "Invalid: 42", "Invalid: 43" }, observed.Order());

        Task<Rail> plainTask = Rail.OkAsync()
            .EnsureAsync(() => false, () => new DomainError("plain task"))
            .TapErrorAsync(async _ => { await Task.Yield(); });
        ValueTask<Rail> plainValueTask = ValueTask.FromResult(Rail.Ok())
            .EnsureAsync(() => false, () => new DomainError("plain value task"))
            .TapErrorAsync(async _ => { await Task.Yield(); });
        Assert.True((await plainTask).IsFailure);
        Assert.True((await plainValueTask).IsFailure);
    }

}
