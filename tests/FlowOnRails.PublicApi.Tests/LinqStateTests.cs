using FlowOnRails.Extensions.Linq;
using FlowOnRails.Extensions.Tasks;

namespace FlowOnRails.PublicApi.Tests;

public class LinqStateTests
{
    public enum State { Success, Failure, Uninitialized }
    private sealed record DomainError(string Message) : Error;
    private sealed record ErrorGroup(IReadOnlyList<Error> Errors) : Error;

    public static TheoryData<State, State> QueryCases
    {
        get
        {
            var cases = new TheoryData<State, State>();
            foreach (var source in Enum.GetValues<State>())
            foreach (var next in Enum.GetValues<State>())
                cases.Add(source, next);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(QueryCases))]
    public void SelectMany_StopsAtTheFirstFailureAndProjectsOnlySuccessfulPairs(State sourceState, State nextState)
    {
        var firstError = new DomainError("first");
        var nextError = new DomainError("next");
        var source = Create(sourceState, 7, firstError);
        var bindCalls = 0;
        var projectCalls = 0;

        RailFor<int> Execute() => source.SelectMany(value =>
        {
            bindCalls++;
            Assert.Equal(7, value);
            return Create(nextState, 42, nextError);
        }, (left, right) =>
        {
            projectCalls++;
            Assert.Equal(7, left);
            Assert.Equal(42, right);
            return left + right;
        });

        if (sourceState == State.Uninitialized ||
            (sourceState != State.Failure && nextState == State.Uninitialized))
        {
            Assert.Throws<InvalidOperationException>(() => Execute());
            Assert.Equal(sourceState == State.Uninitialized ? 0 : 1, bindCalls);
            Assert.Equal(0, projectCalls);
            return;
        }

        var result = Execute();
        Assert.Equal(sourceState == State.Failure ? 0 : 1, bindCalls);
        if (sourceState == State.Failure || nextState == State.Failure)
        {
            Assert.Equal(0, projectCalls);
            Assert.False(result.TryUnwrap(out _, out var error));
            Assert.Same(sourceState == State.Failure ? firstError : nextError, error);
        }
        else
        {
            Assert.Equal(1, projectCalls);
            Assert.True(result.TryUnwrap(out var value, out _));
            Assert.Equal(49, value);
        }
    }

    [Theory]
    [InlineData(State.Success)]
    [InlineData(State.Failure)]
    [InlineData(State.Uninitialized)]
    public void Select_PreservesTheStateAndErrorAndCallsTheProjectionOnceOnSuccess(State state)
    {
        var error = new DomainError("original");
        var source = Create(state, 7, error);
        var calls = 0;
        RailFor<string> Execute() => source.Select(value =>
        {
            calls++;
            Assert.Equal(7, value);
            return "projected";
        });

        if (state == State.Uninitialized)
        {
            Assert.Throws<InvalidOperationException>(() => Execute());
            Assert.Equal(0, calls);
            return;
        }
        var result = Execute();
        Assert.Equal(source.IsSuccess, result.IsSuccess);
        source.TryUnwrap(out _, out var originalError);
        result.TryUnwrap(out _, out var resultError);
        Assert.Same(originalError, resultError);
        Assert.Equal(state == State.Failure ? 0 : 1, calls);
        Assert.Equal(state != State.Failure, result.TryUnwrap(out var value, out _));
        Assert.Equal(state == State.Failure ? null : "projected", value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SequenceMapAndBind_AreDeferredAndProcessEachSuccessfulElementOncePerEnumeration(bool bind)
    {
        var error = new ErrorGroup([new DomainError("one"), new DomainError("two")]);
        var visited = new List<int>();
        var source = new[] { Rail.Ok(1), Rail.Fail<int>(error), Rail.Ok(3) };
        int Project(int value)
        {
            visited.Add(value);
            return value * 10;
        }
        var query = bind ? source.Bind(value => Rail.Ok(Project(value))) : source.Map(Project);
        Assert.Empty(visited);

        // A failure belongs to its element; it does not stop this sequence projection.
        for (var enumeration = 1; enumeration <= 2; enumeration++)
        {
            var results = query.ToArray();
            Assert.Equal(enumeration == 1 ? new[] { 1, 3 } : new[] { 1, 3, 1, 3 }, visited);
            Assert.Equal(3, results.Length);
            Assert.Equal(Rail.Ok(10), results[0]);
            Assert.False(results[1].TryUnwrap(out _, out var failure));
            Assert.Same(error, failure);
            Assert.Equal(Rail.Ok(30), results[2]);
        }
    }

    [Theory]
    [InlineData("Map")]
    [InlineData("Bind")]
    [InlineData("SelectOks")]
    [InlineData("SelectErrors")]
    [InlineData("Partition")]
    [InlineData("Combine")]
    public void SequenceOperations_RejectUninitializedElements(string operation)
    {
        var source = new[] { default(RailFor<int>) };
        var calls = 0;
        Assert.Throws<InvalidOperationException>(() =>
        {
            switch (operation)
            {
                case "Map": source.Map(value => { calls++; return value; }).ToArray(); break;
                case "Bind": source.Bind(value => { calls++; return Rail.Ok(value); }).ToArray(); break;
                case "SelectOks": source.SelectOks().ToArray(); break;
                case "SelectErrors": source.SelectErrors().ToArray(); break;
                case "Partition": source.Partition(); break;
                case "Combine": source.Combine(); break;
                default: throw new ArgumentOutOfRangeException(nameof(operation));
            }
        });
        Assert.Equal(0, calls);
    }

    [Fact]
    public void SequenceFiltersAndPartition_PreserveValuesAndOpaqueErrors()
    {
        var error = new ErrorGroup([new DomainError("one"), new DomainError("two")]);
        var other = new DomainError("other");
        var source = new[] { Rail.Ok(1), Rail.Fail<int>(error),
            Rail.Ok(3), Rail.Fail<int>(other) };

        Assert.Equal(new[] { 1, 3 }, source.SelectOks());
        var errors = source.SelectErrors().ToArray();
        Assert.Equal(2, errors.Length);
        Assert.Same(error, errors[0]);
        Assert.Same(other, errors[1]);
        var partition = source.Partition();
        Assert.Equal(new[] { 1, 3 }, partition.Oks);
        Assert.Equal(2, partition.Errors.Count);
        Assert.Same(error, partition.Errors[0]);
        Assert.Same(other, partition.Errors[1]);
    }

    [Fact]
    public void Combine_StopsEnumeratingAtTheFirstFailureAndDisposesTheEnumerator()
    {
        var error = new ErrorGroup([new DomainError("one"), new DomainError("two")]);
        var disposed = false;
        IEnumerable<RailFor<int>> Source()
        {
            try
            {
                yield return Rail.Ok(1);
                yield return Rail.Fail<int>(error);
                throw new InvalidOperationException("Elements after the failure must not be requested.");
            }
            finally { disposed = true; }
        }

        var result = Source().Combine();
        Assert.False(result.TryUnwrap(out var values, out var failure));
        Assert.Null(values);
        Assert.Same(error, failure);
        Assert.True(disposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Pipeline_DoesNotExecuteStepsAfterTheFirstFailure(int family)
    {
        var error = new DomainError("saving failed");
        var saved = 0;
        var notified = 0;
        var projected = 0;
        RailFor<int> Save(int value) { saved++; Assert.Equal(7, value); return Rail.Fail<int>(error); }
        void Notify(int _) => notified++;
        string Project(int _) { projected++; return "must not run"; }
        var source = Rail.Ok(7);

        var result = family switch
        {
            0 => source.Bind(Save).Tap(Notify).Map(Project),
            1 => await Task.FromResult(source).BindAsync(value => Task.FromResult(Save(value)))
                .TapAsync(Notify).MapAsync(Project),
            2 => await ValueTask.FromResult(source).BindAsync(value => ValueTask.FromResult(Save(value)))
                .TapAsync(Notify).MapAsync(Project),
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };
        Assert.Equal((1, 0, 0), (saved, notified, projected));
        Assert.False(result.TryUnwrap(out _, out var failure));
        Assert.Same(error, failure);
    }

    private static RailFor<int> Create(State state, int value, Error error) => state switch
    {
        State.Success => Rail.Ok(value),
        State.Failure => Rail.Fail<int>(error),
        State.Uninitialized => default,
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };
}
