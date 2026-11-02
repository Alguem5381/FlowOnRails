using FlowOnRails.Extensions.Tasks;

namespace FlowOnRails.PublicApi.Tests;

/// <summary>
/// The same public contract applies to both rail shapes and all three execution families.
/// Each data row runs one operation, so failures identify the state and overload involved.
/// Pending awaits, exceptions and cancellation are covered in AsyncCompositionTests.
/// </summary>
public class OperationStateTests
{
    public enum State { Neutral, Rich, Failure, Uninitialized }
    public enum Family { Sync, Task, ValueTask, TaskValueTaskBridge }
    public enum Operation
    {
        Map, MapAwait, BindValueNeutral, BindValueRich, BindValueFailure,
        BindVoidNeutral, BindVoidRich, BindVoidFailure, Tap, TapAwait,
        TapIfTrue, TapIfFalse, TapIfPredicateTrue, TapIfPredicateFalse,
        EnsureTrue, EnsureFalse, MapError, MapErrorAwait, Recover,
        RecoverWithNeutral, RecoverWithRich, RecoverWithFailure, Match, MatchRich
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
                if (family == Family.Sync && operation is
                    Operation.MapAwait or Operation.TapAwait or Operation.MapErrorAwait)
                    continue;
                if (family == Family.TaskValueTaskBridge && (!typed || operation is not
                    (Operation.BindValueNeutral or Operation.BindValueRich or Operation.BindValueFailure
                    or Operation.BindVoidNeutral or Operation.BindVoidRich or Operation.BindVoidFailure)))
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
        var probe = new Probe(state, typed);
        var plain = state switch
        {
            State.Neutral => Rail.Ok(),
            State.Rich => Rail.Succeed(probe.Rich),
            State.Failure => Rail.Fail(probe.OriginalError),
            _ => default
        };
        var valued = state switch
        {
            State.Neutral => Rail.Ok(7),
            State.Rich => Rail.Ok(7, probe.Rich),
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
            // Assert on the operation itself: observing a returned default later would
            // mask an operation that incorrectly accepted an uninitialized source.
            await Assert.ThrowsAsync<InvalidOperationException>(Execute);
            Assert.Equal((0, 0, 0), (probe.SuccessCalls, probe.ErrorCalls, probe.PredicateCalls));
            return;
        }

        var result = Observe(await Execute());
        var failureOperation = operation is Operation.MapError or Operation.MapErrorAwait
            or Operation.Recover or Operation.RecoverWithNeutral
            or Operation.RecoverWithRich or Operation.RecoverWithFailure;
        var match = operation is Operation.Match or Operation.MatchRich;
        var predicateOperation = operation is Operation.TapIfPredicateTrue
            or Operation.TapIfPredicateFalse or Operation.EnsureTrue or Operation.EnsureFalse;
        var skipAction = predicateOperation || operation == Operation.TapIfFalse;
        var successfulSource = state != State.Failure;

        Assert.Equal(successfulSource && !failureOperation && (!skipAction ||
            operation == Operation.TapIfPredicateTrue) ? 1 : 0, probe.SuccessCalls);
        Assert.Equal(!successfulSource && (failureOperation || match) ? 1 : 0, probe.ErrorCalls);
        Assert.Equal(successfulSource && predicateOperation ? 1 : 0, probe.PredicateCalls);

        Outcome expectedOutcome = state == State.Rich ? probe.Rich : probe.Neutral;
        int? expectedValue = typed ? 7 : null;
        if (successfulSource)
        {
            expectedOutcome = operation switch
            {
                Operation.BindValueRich or Operation.BindVoidRich => probe.NextSuccess,
                Operation.BindValueFailure or Operation.BindVoidFailure or Operation.EnsureFalse => probe.NextError,
                _ => expectedOutcome
            };
            expectedValue = operation switch
            {
                Operation.Map or Operation.MapAwait or Operation.BindValueNeutral
                    or Operation.BindValueRich => 42,
                Operation.BindVoidNeutral or Operation.BindVoidRich => null,
                _ => expectedValue
            };
        }
        else
        {
            expectedOutcome = operation switch
            {
                Operation.MapError or Operation.MapErrorAwait or Operation.RecoverWithFailure => probe.NextError,
                Operation.RecoverWithRich => probe.NextSuccess,
                Operation.RecoverWithNeutral => probe.Neutral,
                Operation.Recover => typed ? probe.Neutral : probe.NextSuccess,
                _ => probe.OriginalError
            };
            expectedValue = typed && operation is (Operation.Recover or Operation.RecoverWithNeutral
                or Operation.RecoverWithRich) ? 42 : null;
        }
        if (expectedOutcome is Error)
            expectedValue = null;

        Assert.Same(expectedOutcome, result.Outcome);
        Assert.Equal(expectedValue, result.Value);
    }

    private sealed record DomainSuccess(string Name) : Success;
    private sealed record DomainError(string Message) : Error;
    private sealed record ErrorGroup(IReadOnlyList<Error> Errors) : Error;
    private sealed record Observation(Outcome Outcome, int? Value);

    private static Observation Observe(object result)
    {
        if (result is Observation observation)
            return observation;
        if (result is Rail plain)
        {
            var ok = plain.TryUnwrap(out var error, out var success);
            Assert.Equal(ok, plain.IsSuccess);
            Assert.Equal(!ok, plain.IsFailure);
            Assert.Equal(ok, plain.TryUnwrap(out var simpleError));
            Assert.Same(error, simpleError);
            Assert.Same(ok ? (Outcome?)success : error, plain.Outcome);
            Assert.Null(ok ? error : (Outcome?)success);
            return new(plain.Outcome, null);
        }
        var valued = Assert.IsType<RailFor<int>>(result);
        var successFlag = valued.TryUnwrap(out var value, out var failure, out var semantic);
        Assert.Equal(successFlag, valued.IsSuccess);
        Assert.Equal(!successFlag, valued.IsFailure);
        Assert.Equal(successFlag, valued.TryUnwrap(out var simpleValue, out var simpleFailure));
        Assert.Equal(value, simpleValue);
        Assert.Same(failure, simpleFailure);
        Assert.Same(successFlag ? (Outcome?)semantic : failure, valued.Outcome);
        Assert.Null(successFlag ? failure : (Outcome?)semantic);
        if (!successFlag)
            Assert.Equal(0, value);
        return new(valued.Outcome, successFlag ? value : null);
    }

    private sealed class Probe(State state, bool typed)
    {
        public readonly Success Neutral = (Success)Rail.Ok().Outcome;
        public readonly Success Rich = new DomainSuccess("original");
        public readonly Success NextSuccess = new DomainSuccess("next");
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

        public Observation Match(int? value) =>
            Success(value, new Observation(state == State.Rich ? Rich : Neutral, value));

        public Observation MatchRich(int? value, Success success)
        {
            Assert.Same(state == State.Rich ? Rich : Neutral, success);
            return Match(value);
        }

        public Observation MatchFailure(Error error) => Failure(error, new Observation(error, null));
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
            Operation.Recover => rail.Recover(error => p.Failure(error, p.NextSuccess)),
            Operation.Match => rail.Match(() => p.Match(null), p.MatchFailure),
            Operation.MatchRich => rail.Match(success => p.MatchRich(null, success), p.MatchFailure),
            Operation.BindValueNeutral => rail.Bind(() => p.Success(null, Rail.Ok(42))),
            Operation.BindValueRich => rail.Bind(() => p.Success(null, Rail.Ok(42, p.NextSuccess))),
            Operation.BindValueFailure => rail.Bind(() => p.Success(null, Rail.Fail<int>(p.NextError))),
            Operation.BindVoidNeutral => rail.Bind(() => p.Success(null, Rail.Ok())),
            Operation.BindVoidRich => rail.Bind(() => p.Success(null, Rail.Succeed(p.NextSuccess))),
            Operation.BindVoidFailure => rail.Bind(() => p.Success(null, Rail.Fail(p.NextError))),
            Operation.RecoverWithNeutral => rail.RecoverWith(error => p.Failure(error, Rail.Ok())),
            Operation.RecoverWithRich => rail.RecoverWith(error => p.Failure(error, Rail.Succeed(p.NextSuccess))),
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
            Operation.MatchRich => rail.Match((value, success) => p.MatchRich(value, success), p.MatchFailure),
            Operation.BindValueNeutral => rail.Bind(value => p.Success(value, Rail.Ok(42))),
            Operation.BindValueRich => rail.Bind(value => p.Success(value, Rail.Ok(42, p.NextSuccess))),
            Operation.BindValueFailure => rail.Bind(value => p.Success(value, Rail.Fail<int>(p.NextError))),
            Operation.BindVoidNeutral => rail.Bind(value => p.Success(value, Rail.Ok())),
            Operation.BindVoidRich => rail.Bind(value => p.Success(value, Rail.Succeed(p.NextSuccess))),
            Operation.BindVoidFailure => rail.Bind(value => p.Success(value, Rail.Fail(p.NextError))),
            Operation.RecoverWithNeutral => rail.RecoverWith(error => p.Failure(error, Rail.Ok(42))),
            Operation.RecoverWithRich => rail.RecoverWith(error => p.Failure(error, Rail.Ok(42, p.NextSuccess))),
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
            Operation.Recover => await source.RecoverAsync(error => Task.FromResult(p.Failure(error, p.NextSuccess))),
            Operation.Match => await source.MatchAsync(() => p.Match(null), p.MatchFailure),
            Operation.MatchRich => await source.MatchAsync(success => p.MatchRich(null, success), p.MatchFailure),
            Operation.BindValueNeutral => await source.BindAsync(() => Task.FromResult(p.Success(null, Rail.Ok(42)))),
            Operation.BindValueRich => await source.BindAsync(() => Task.FromResult(p.Success(null, Rail.Ok(42, p.NextSuccess)))),
            Operation.BindValueFailure => await source.BindAsync(() => Task.FromResult(p.Success(null, Rail.Fail<int>(p.NextError)))),
            Operation.BindVoidNeutral => await source.BindAsync(() => Task.FromResult(p.Success(null, Rail.Ok()))),
            Operation.BindVoidRich => await source.BindAsync(() => Task.FromResult(p.Success(null, Rail.Succeed(p.NextSuccess)))),
            Operation.BindVoidFailure => await source.BindAsync(() => Task.FromResult(p.Success(null, Rail.Fail(p.NextError)))),
            Operation.RecoverWithNeutral => await source.RecoverWithAsync(error => Task.FromResult(p.Failure(error, Rail.Ok()))),
            Operation.RecoverWithRich => await source.RecoverWithAsync(error => Task.FromResult(p.Failure(error, Rail.Succeed(p.NextSuccess)))),
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
            Operation.MatchRich => await source.MatchAsync((value, success) => p.MatchRich(value, success), p.MatchFailure),
            Operation.BindValueNeutral => await source.BindAsync(value => Task.FromResult(p.Success(value, Rail.Ok(42)))),
            Operation.BindValueRich => await source.BindAsync(value => Task.FromResult(p.Success(value, Rail.Ok(42, p.NextSuccess)))),
            Operation.BindValueFailure => await source.BindAsync(value => Task.FromResult(p.Success(value, Rail.Fail<int>(p.NextError)))),
            Operation.BindVoidNeutral => await source.BindAsync(value => Task.FromResult(p.Success(value, Rail.Ok()))),
            Operation.BindVoidRich => await source.BindAsync(value => Task.FromResult(p.Success(value, Rail.Succeed(p.NextSuccess)))),
            Operation.BindVoidFailure => await source.BindAsync(value => Task.FromResult(p.Success(value, Rail.Fail(p.NextError)))),
            Operation.RecoverWithNeutral => await source.RecoverWithAsync(error => Task.FromResult(p.Failure(error, Rail.Ok(42)))),
            Operation.RecoverWithRich => await source.RecoverWithAsync(error => Task.FromResult(p.Failure(error, Rail.Ok(42, p.NextSuccess)))),
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
            Operation.Recover => await source.RecoverAsync(error => ValueTask.FromResult(p.Failure(error, p.NextSuccess))),
            Operation.Match => await source.MatchAsync(() => p.Match(null), p.MatchFailure),
            Operation.MatchRich => await source.MatchAsync(success => p.MatchRich(null, success), p.MatchFailure),
            Operation.BindValueNeutral => await source.BindAsync(() => ValueTask.FromResult(p.Success(null, Rail.Ok(42)))),
            Operation.BindValueRich => await source.BindAsync(() => ValueTask.FromResult(p.Success(null, Rail.Ok(42, p.NextSuccess)))),
            Operation.BindValueFailure => await source.BindAsync(() => ValueTask.FromResult(p.Success(null, Rail.Fail<int>(p.NextError)))),
            Operation.BindVoidNeutral => await source.BindAsync(() => ValueTask.FromResult(p.Success(null, Rail.Ok()))),
            Operation.BindVoidRich => await source.BindAsync(() => ValueTask.FromResult(p.Success(null, Rail.Succeed(p.NextSuccess)))),
            Operation.BindVoidFailure => await source.BindAsync(() => ValueTask.FromResult(p.Success(null, Rail.Fail(p.NextError)))),
            Operation.RecoverWithNeutral => await source.RecoverWithAsync(error => ValueTask.FromResult(p.Failure(error, Rail.Ok()))),
            Operation.RecoverWithRich => await source.RecoverWithAsync(error => ValueTask.FromResult(p.Failure(error, Rail.Succeed(p.NextSuccess)))),
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
            Operation.MatchRich => await source.MatchAsync((value, success) => p.MatchRich(value, success), p.MatchFailure),
            Operation.BindValueNeutral => await source.BindAsync(value => ValueTask.FromResult(p.Success(value, Rail.Ok(42)))),
            Operation.BindValueRich => await source.BindAsync(value => ValueTask.FromResult(p.Success(value, Rail.Ok(42, p.NextSuccess)))),
            Operation.BindValueFailure => await source.BindAsync(value => ValueTask.FromResult(p.Success(value, Rail.Fail<int>(p.NextError)))),
            Operation.BindVoidNeutral => await source.BindAsync(value => ValueTask.FromResult(p.Success(value, Rail.Ok()))),
            Operation.BindVoidRich => await source.BindAsync(value => ValueTask.FromResult(p.Success(value, Rail.Succeed(p.NextSuccess)))),
            Operation.BindVoidFailure => await source.BindAsync(value => ValueTask.FromResult(p.Success(value, Rail.Fail(p.NextError)))),
            Operation.RecoverWithNeutral => await source.RecoverWithAsync(error => ValueTask.FromResult(p.Failure(error, Rail.Ok(42)))),
            Operation.RecoverWithRich => await source.RecoverWithAsync(error => ValueTask.FromResult(p.Failure(error, Rail.Ok(42, p.NextSuccess)))),
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
            Operation.BindValueNeutral => (object)await source.BindValueTaskAsync(value => ValueTask.FromResult(p.Success(value, Rail.Ok(42)))),
            Operation.BindValueRich => (object)await source.BindValueTaskAsync(value => ValueTask.FromResult(p.Success(value, Rail.Ok(42, p.NextSuccess)))),
            Operation.BindValueFailure => (object)await source.BindValueTaskAsync(value => ValueTask.FromResult(p.Success(value, Rail.Fail<int>(p.NextError)))),
            Operation.BindVoidNeutral => (object)await source.BindValueTaskAsync(value => ValueTask.FromResult(p.Success(value, Rail.Ok()))),
            Operation.BindVoidRich => (object)await source.BindValueTaskAsync(value => ValueTask.FromResult(p.Success(value, Rail.Succeed(p.NextSuccess)))),
            Operation.BindVoidFailure => (object)await source.BindValueTaskAsync(value => ValueTask.FromResult(p.Success(value, Rail.Fail(p.NextError)))),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }
}
