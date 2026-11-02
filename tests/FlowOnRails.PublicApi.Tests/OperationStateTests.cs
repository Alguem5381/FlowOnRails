using FlowOnRails.Extensions.Tasks;

namespace FlowOnRails.PublicApi.Tests;

/// <summary>
/// Tests the public contract across successful, failed and uninitialized rails.
/// Pending awaits, exceptions and cancellation are covered in AsyncCompositionTests.
/// </summary>
public class OperationStateTests
{
    public enum State { Success, Failure, Uninitialized }
    public enum Family { Sync, Task, ValueTask, TaskValueTaskBridge }
    public enum Operation
    {
        Map, MapAwait, BindValueSuccess, BindValueFailure,
        BindVoidSuccess, BindVoidFailure, Tap, TapAwait,
        TapIfTrue, TapIfFalse, TapIfPredicateTrue, TapIfPredicateFalse,
        EnsureTrue, EnsureFalse, MapError, MapErrorAwait, Recover,
        RecoverWithSuccess, RecoverWithFailure, Match
    }

    public static TheoryData<State, Family, Operation, bool> Cases
    {
        get
        {
            var cases = new TheoryData<State, Family, Operation, bool>();
            foreach (var state in Enum.GetValues<State>())
            foreach (var family in Enum.GetValues<Family>())
            foreach (var operation in Enum.GetValues<Operation>())
            foreach (var typed in new[] { false, true })
            {
                if (!typed && operation == Operation.Recover)
                    continue;
                if (family == Family.Sync && operation is
                    Operation.MapAwait or Operation.TapAwait or Operation.MapErrorAwait)
                    continue;
                if (family == Family.TaskValueTaskBridge && (!typed || operation is not
                    (Operation.BindValueSuccess or Operation.BindValueFailure
                    or Operation.BindVoidSuccess or Operation.BindVoidFailure)))
                    continue;
                cases.Add(state, family, operation, typed);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Operations_RespectStateAndInvokeOnlyTheApplicableCallbacks(
        State state, Family family, Operation operation, bool typed)
    {
        var probe = new Probe(typed);
        var plain = state switch
        {
            State.Success => Rail.Ok(),
            State.Failure => Rail.Fail(probe.OriginalError),
            _ => default
        };
        var valued = state switch
        {
            State.Success => Rail.Ok(7),
            State.Failure => Rail.Fail<int>(probe.OriginalError),
            _ => default
        };

        Task<object> Execute() => (family, typed) switch
        {
            (Family.Sync, false) => System.Threading.Tasks.Task.FromResult(RunSync(plain, operation, probe)),
            (Family.Sync, true) => System.Threading.Tasks.Task.FromResult(RunSync(valued, operation, probe)),
            (Family.Task, false) => RunTask(plain, operation, probe),
            (Family.Task, true) => RunTask(valued, operation, probe),
            (Family.ValueTask, false) => RunValueTask(plain, operation, probe),
            (Family.ValueTask, true) => RunValueTask(valued, operation, probe),
            (Family.TaskValueTaskBridge, true) => RunBridge(valued, operation, probe),
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };

        if (state == State.Uninitialized)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(Execute);
            Assert.Equal((0, 0, 0), (probe.SuccessCalls, probe.ErrorCalls, probe.PredicateCalls));
            return;
        }

        var result = Observe(await Execute());
        var failureOperation = operation is Operation.MapError or Operation.MapErrorAwait
            or Operation.Recover or Operation.RecoverWithSuccess or Operation.RecoverWithFailure;
        var predicateOperation = operation is Operation.TapIfPredicateTrue
            or Operation.TapIfPredicateFalse or Operation.EnsureTrue or Operation.EnsureFalse;
        var skipAction = predicateOperation || operation == Operation.TapIfFalse;
        var successfulSource = state == State.Success;

        Assert.Equal(successfulSource && !failureOperation && (!skipAction ||
            operation == Operation.TapIfPredicateTrue) ? 1 : 0, probe.SuccessCalls);
        Assert.Equal(!successfulSource && (failureOperation || operation == Operation.Match) ? 1 : 0, probe.ErrorCalls);
        Assert.Equal(successfulSource && predicateOperation ? 1 : 0, probe.PredicateCalls);

        Error? expectedError = null;
        int? expectedValue = typed ? 7 : null;
        if (successfulSource)
        {
            if (operation is Operation.BindValueFailure or Operation.BindVoidFailure or Operation.EnsureFalse)
                expectedError = probe.NextError;
            expectedValue = operation switch
            {
                Operation.Map or Operation.MapAwait or Operation.BindValueSuccess => 42,
                Operation.BindVoidSuccess => null,
                _ => expectedValue
            };
        }
        else
        {
            expectedError = operation switch
            {
                Operation.MapError or Operation.MapErrorAwait or Operation.RecoverWithFailure => probe.NextError,
                Operation.Recover or Operation.RecoverWithSuccess => null,
                _ => probe.OriginalError
            };
            expectedValue = typed && operation is (Operation.Recover or Operation.RecoverWithSuccess) ? 42 : null;
        }
        if (expectedError is not null)
            expectedValue = null;

        Assert.Equal(expectedError is null, result.IsSuccess);
        Assert.Same(expectedError, result.Error);
        Assert.Equal(expectedValue, result.Value);
    }

    private sealed record DomainError(string Message) : Error;
    private sealed record ErrorGroup(IReadOnlyList<Error> Errors) : Error;
    private sealed record Observation(bool IsSuccess, Error? Error, int? Value);

    private static Observation Observe(object result)
    {
        if (result is Observation observation)
            return observation;
        if (result is Rail plain)
        {
            var ok = plain.TryUnwrap(out var error);
            Assert.Equal(ok, plain.IsSuccess);
            Assert.Equal(!ok, plain.IsFailure);
            Assert.Equal(ok, error is null);
            return new(ok, error, null);
        }
        var valued = Assert.IsType<RailFor<int>>(result);
        var successFlag = valued.TryUnwrap(out var value, out var failure);
        Assert.Equal(successFlag, valued.IsSuccess);
        Assert.Equal(!successFlag, valued.IsFailure);
        Assert.Equal(successFlag, failure is null);
        if (!successFlag)
            Assert.Equal(0, value);
        return new(successFlag, failure, successFlag ? value : null);
    }

    private sealed class Probe(bool typed)
    {
        public readonly Error OriginalError = new ErrorGroup([new DomainError("first"), new DomainError("second")]);
        public readonly Error NextError = new DomainError("replacement");
        public int SuccessCalls;
        public int ErrorCalls;
        public int PredicateCalls;

        public T Success<T>(int? value, T result)
        {
            SuccessCalls++;
            Assert.Equal(typed ? 7 : (int?)null, value);
            return result;
        }

        public void Tap(int? value) => Success(value, 0);

        public bool Predicate(int? value, bool result)
        {
            PredicateCalls++;
            Assert.Equal(typed ? 7 : (int?)null, value);
            return result;
        }

        public T Failure<T>(Error error, T result)
        {
            ErrorCalls++;
            Assert.Same(OriginalError, error);
            return result;
        }

        public Observation Match(int? value) => Success(value, new Observation(true, null, value));
        public Observation MatchFailure(Error error) => Failure(error, new Observation(false, error, null));
    }

    private static object RunSync(Rail rail, Operation operation, Probe p)
    {
        return operation switch
        {
            Operation.Map => rail.Map(() => p.Success(null, 42)),
            Operation.Tap => rail.Tap(() => p.Tap(null)),
            Operation.EnsureTrue => rail.Ensure(() => p.Predicate(null, true), p.NextError),
            Operation.EnsureFalse => rail.Ensure(() => p.Predicate(null, false), p.NextError),
            Operation.MapError => rail.MapError(error => p.Failure(error, p.NextError)),
            Operation.Match => rail.Match(() => p.Match(null), p.MatchFailure),
            Operation.BindValueSuccess => rail.Bind(() => p.Success(null, Rail.Ok(42))),
            Operation.BindValueFailure => rail.Bind(() => p.Success(null, Rail.Fail<int>(p.NextError))),
            Operation.BindVoidSuccess => rail.Bind(() => p.Success(null, Rail.Ok())),
            Operation.BindVoidFailure => rail.Bind(() => p.Success(null, Rail.Fail(p.NextError))),
            Operation.RecoverWithSuccess => rail.RecoverWith(error => p.Failure(error, Rail.Ok())),
            Operation.RecoverWithFailure => rail.RecoverWith(error => p.Failure(error, Rail.Fail(p.NextError))),
            Operation.TapIfTrue => rail.TapIf(true, () => p.Tap(null)),
            Operation.TapIfFalse => rail.TapIf(false, () => p.Tap(null)),
            Operation.TapIfPredicateTrue => rail.TapIf(() => p.Predicate(null, true), () => p.Tap(null)),
            Operation.TapIfPredicateFalse => rail.TapIf(() => p.Predicate(null, false), () => p.Tap(null)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static object RunSync(RailFor<int> rail, Operation operation, Probe p)
    {
        return operation switch
        {
            Operation.Map => rail.Map(value => p.Success(value, 42)),
            Operation.Tap => rail.Tap(value => p.Tap(value)),
            Operation.EnsureTrue => rail.Ensure(value => p.Predicate(value, true), p.NextError),
            Operation.EnsureFalse => rail.Ensure(value => p.Predicate(value, false), p.NextError),
            Operation.MapError => rail.MapError(error => p.Failure(error, p.NextError)),
            Operation.Recover => rail.Recover(error => p.Failure(error, 42)),
            Operation.Match => rail.Match(value => p.Match(value), p.MatchFailure),
            Operation.BindValueSuccess => rail.Bind(value => p.Success(value, Rail.Ok(42))),
            Operation.BindValueFailure => rail.Bind(value => p.Success(value, Rail.Fail<int>(p.NextError))),
            Operation.BindVoidSuccess => rail.Bind(value => p.Success(value, Rail.Ok())),
            Operation.BindVoidFailure => rail.Bind(value => p.Success(value, Rail.Fail(p.NextError))),
            Operation.RecoverWithSuccess => rail.RecoverWith(error => p.Failure(error, Rail.Ok(42))),
            Operation.RecoverWithFailure => rail.RecoverWith(error => p.Failure(error, Rail.Fail<int>(p.NextError))),
            Operation.TapIfTrue => rail.TapIf(true, value => p.Tap(value)),
            Operation.TapIfFalse => rail.TapIf(false, value => p.Tap(value)),
            Operation.TapIfPredicateTrue => rail.TapIf(value => p.Predicate(value, true), value => p.Tap(value)),
            Operation.TapIfPredicateFalse => rail.TapIf(value => p.Predicate(value, false), value => p.Tap(value)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static async Task<object> RunTask(Rail rail, Operation operation, Probe p)
    {
        var source = Task.FromResult(rail);
        return operation switch
        {
            Operation.Map => await source.MapAsync(() => p.Success(null, 42)),
            Operation.Tap => await source.TapAsync(() => p.Tap(null)),
            Operation.EnsureTrue => await source.EnsureAsync(() => p.Predicate(null, true), p.NextError),
            Operation.EnsureFalse => await source.EnsureAsync(() => p.Predicate(null, false), p.NextError),
            Operation.MapError => await source.MapErrorAsync(error => p.Failure(error, p.NextError)),
            Operation.Match => await source.MatchAsync(() => p.Match(null), p.MatchFailure),
            Operation.BindValueSuccess => await source.BindAsync(() => Task.FromResult(p.Success(null, Rail.Ok(42)))),
            Operation.BindValueFailure => await source.BindAsync(() => Task.FromResult(p.Success(null, Rail.Fail<int>(p.NextError)))),
            Operation.BindVoidSuccess => await source.BindAsync(() => Task.FromResult(p.Success(null, Rail.Ok()))),
            Operation.BindVoidFailure => await source.BindAsync(() => Task.FromResult(p.Success(null, Rail.Fail(p.NextError)))),
            Operation.RecoverWithSuccess => await source.RecoverWithAsync(error => Task.FromResult(p.Failure(error, Rail.Ok()))),
            Operation.RecoverWithFailure => await source.RecoverWithAsync(error => Task.FromResult(p.Failure(error, Rail.Fail(p.NextError)))),
            Operation.TapIfTrue => await source.TapIfAsync(true, () => { p.Tap(null); return Task.CompletedTask; }),
            Operation.TapIfFalse => await source.TapIfAsync(false, () => { p.Tap(null); return Task.CompletedTask; }),
            Operation.TapIfPredicateTrue => await source.TapIfAsync(() => p.Predicate(null, true), () => { p.Tap(null); return Task.CompletedTask; }),
            Operation.TapIfPredicateFalse => await source.TapIfAsync(() => p.Predicate(null, false), () => { p.Tap(null); return Task.CompletedTask; }),
            Operation.MapAwait => await source.MapAsync(() => Task.FromResult(p.Success(null, 42))),
            Operation.TapAwait => await source.TapAsync(() => { p.Tap(null); return Task.CompletedTask; }),
            Operation.MapErrorAwait => await source.MapErrorAsync(error => Task.FromResult(p.Failure(error, p.NextError))),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static async Task<object> RunTask(RailFor<int> rail, Operation operation, Probe p)
    {
        var source = Task.FromResult(rail);
        return operation switch
        {
            Operation.Map => await source.MapAsync(value => p.Success(value, 42)),
            Operation.Tap => await source.TapAsync(value => p.Tap(value)),
            Operation.EnsureTrue => await source.EnsureAsync(value => p.Predicate(value, true), p.NextError),
            Operation.EnsureFalse => await source.EnsureAsync(value => p.Predicate(value, false), p.NextError),
            Operation.MapError => await source.MapErrorAsync(error => p.Failure(error, p.NextError)),
            Operation.Recover => await source.RecoverAsync(error => Task.FromResult(p.Failure(error, 42))),
            Operation.Match => await source.MatchAsync(value => p.Match(value), p.MatchFailure),
            Operation.BindValueSuccess => await source.BindAsync(value => Task.FromResult(p.Success(value, Rail.Ok(42)))),
            Operation.BindValueFailure => await source.BindAsync(value => Task.FromResult(p.Success(value, Rail.Fail<int>(p.NextError)))),
            Operation.BindVoidSuccess => await source.BindAsync(value => Task.FromResult(p.Success(value, Rail.Ok()))),
            Operation.BindVoidFailure => await source.BindAsync(value => Task.FromResult(p.Success(value, Rail.Fail(p.NextError)))),
            Operation.RecoverWithSuccess => await source.RecoverWithAsync(error => Task.FromResult(p.Failure(error, Rail.Ok(42)))),
            Operation.RecoverWithFailure => await source.RecoverWithAsync(error => Task.FromResult(p.Failure(error, Rail.Fail<int>(p.NextError)))),
            Operation.TapIfTrue => await source.TapIfAsync(true, value => { p.Tap(value); return Task.CompletedTask; }),
            Operation.TapIfFalse => await source.TapIfAsync(false, value => { p.Tap(value); return Task.CompletedTask; }),
            Operation.TapIfPredicateTrue => await source.TapIfAsync(value => p.Predicate(value, true), value => { p.Tap(value); return Task.CompletedTask; }),
            Operation.TapIfPredicateFalse => await source.TapIfAsync(value => p.Predicate(value, false), value => { p.Tap(value); return Task.CompletedTask; }),
            Operation.MapAwait => await source.MapAsync(value => Task.FromResult(p.Success(value, 42))),
            Operation.TapAwait => await source.TapAsync(value => { p.Tap(value); return Task.CompletedTask; }),
            Operation.MapErrorAwait => await source.MapErrorAsync(error => Task.FromResult(p.Failure(error, p.NextError))),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static async Task<object> RunValueTask(Rail rail, Operation operation, Probe p)
    {
        var source = ValueTask.FromResult(rail);
        return operation switch
        {
            Operation.Map => await source.MapAsync(() => p.Success(null, 42)),
            Operation.Tap => await source.TapAsync(() => p.Tap(null)),
            Operation.EnsureTrue => await source.EnsureAsync(() => p.Predicate(null, true), p.NextError),
            Operation.EnsureFalse => await source.EnsureAsync(() => p.Predicate(null, false), p.NextError),
            Operation.MapError => await source.MapErrorAsync(error => p.Failure(error, p.NextError)),
            Operation.Match => await source.MatchAsync(() => p.Match(null), p.MatchFailure),
            Operation.BindValueSuccess => await source.BindAsync(() => ValueTask.FromResult(p.Success(null, Rail.Ok(42)))),
            Operation.BindValueFailure => await source.BindAsync(() => ValueTask.FromResult(p.Success(null, Rail.Fail<int>(p.NextError)))),
            Operation.BindVoidSuccess => await source.BindAsync(() => ValueTask.FromResult(p.Success(null, Rail.Ok()))),
            Operation.BindVoidFailure => await source.BindAsync(() => ValueTask.FromResult(p.Success(null, Rail.Fail(p.NextError)))),
            Operation.RecoverWithSuccess => await source.RecoverWithAsync(error => ValueTask.FromResult(p.Failure(error, Rail.Ok()))),
            Operation.RecoverWithFailure => await source.RecoverWithAsync(error => ValueTask.FromResult(p.Failure(error, Rail.Fail(p.NextError)))),
            Operation.TapIfTrue => await source.TapIfAsync(true, () => { p.Tap(null); return ValueTask.CompletedTask; }),
            Operation.TapIfFalse => await source.TapIfAsync(false, () => { p.Tap(null); return ValueTask.CompletedTask; }),
            Operation.TapIfPredicateTrue => await source.TapIfAsync(() => p.Predicate(null, true), () => { p.Tap(null); return ValueTask.CompletedTask; }),
            Operation.TapIfPredicateFalse => await source.TapIfAsync(() => p.Predicate(null, false), () => { p.Tap(null); return ValueTask.CompletedTask; }),
            Operation.MapAwait => await source.MapAsync(() => ValueTask.FromResult(p.Success(null, 42))),
            Operation.TapAwait => await source.TapAsync(() => { p.Tap(null); return ValueTask.CompletedTask; }),
            Operation.MapErrorAwait => await source.MapErrorAsync(error => ValueTask.FromResult(p.Failure(error, p.NextError))),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static async Task<object> RunValueTask(RailFor<int> rail, Operation operation, Probe p)
    {
        var source = ValueTask.FromResult(rail);
        return operation switch
        {
            Operation.Map => await source.MapAsync(value => p.Success(value, 42)),
            Operation.Tap => await source.TapAsync(value => p.Tap(value)),
            Operation.EnsureTrue => await source.EnsureAsync(value => p.Predicate(value, true), p.NextError),
            Operation.EnsureFalse => await source.EnsureAsync(value => p.Predicate(value, false), p.NextError),
            Operation.MapError => await source.MapErrorAsync(error => p.Failure(error, p.NextError)),
            Operation.Recover => await source.RecoverAsync(error => ValueTask.FromResult(p.Failure(error, 42))),
            Operation.Match => await source.MatchAsync(value => p.Match(value), p.MatchFailure),
            Operation.BindValueSuccess => await source.BindAsync(value => ValueTask.FromResult(p.Success(value, Rail.Ok(42)))),
            Operation.BindValueFailure => await source.BindAsync(value => ValueTask.FromResult(p.Success(value, Rail.Fail<int>(p.NextError)))),
            Operation.BindVoidSuccess => await source.BindAsync(value => ValueTask.FromResult(p.Success(value, Rail.Ok()))),
            Operation.BindVoidFailure => await source.BindAsync(value => ValueTask.FromResult(p.Success(value, Rail.Fail(p.NextError)))),
            Operation.RecoverWithSuccess => await source.RecoverWithAsync(error => ValueTask.FromResult(p.Failure(error, Rail.Ok(42)))),
            Operation.RecoverWithFailure => await source.RecoverWithAsync(error => ValueTask.FromResult(p.Failure(error, Rail.Fail<int>(p.NextError)))),
            Operation.TapIfTrue => await source.TapIfAsync(true, value => { p.Tap(value); return ValueTask.CompletedTask; }),
            Operation.TapIfFalse => await source.TapIfAsync(false, value => { p.Tap(value); return ValueTask.CompletedTask; }),
            Operation.TapIfPredicateTrue => await source.TapIfAsync(value => p.Predicate(value, true), value => { p.Tap(value); return ValueTask.CompletedTask; }),
            Operation.TapIfPredicateFalse => await source.TapIfAsync(value => p.Predicate(value, false), value => { p.Tap(value); return ValueTask.CompletedTask; }),
            Operation.MapAwait => await source.MapAsync(value => ValueTask.FromResult(p.Success(value, 42))),
            Operation.TapAwait => await source.TapAsync(value => { p.Tap(value); return ValueTask.CompletedTask; }),
            Operation.MapErrorAwait => await source.MapErrorAsync(error => ValueTask.FromResult(p.Failure(error, p.NextError))),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }

    private static async Task<object> RunBridge(RailFor<int> rail, Operation operation, Probe p)
    {
        var source = Task.FromResult(rail);
        // Preserve each return shape; a common Rail type would discard the value.
        return operation switch
        {
            Operation.BindValueSuccess => (object)await source.BindValueTaskAsync(value => ValueTask.FromResult(p.Success(value, Rail.Ok(42)))),
            Operation.BindValueFailure => (object)await source.BindValueTaskAsync(value => ValueTask.FromResult(p.Success(value, Rail.Fail<int>(p.NextError)))),
            Operation.BindVoidSuccess => (object)await source.BindValueTaskAsync(value => ValueTask.FromResult(p.Success(value, Rail.Ok()))),
            Operation.BindVoidFailure => (object)await source.BindValueTaskAsync(value => ValueTask.FromResult(p.Success(value, Rail.Fail(p.NextError)))),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }
}
