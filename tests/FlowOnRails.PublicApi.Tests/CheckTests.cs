namespace FlowOnRails.PublicApi.Tests;

public class CheckTests
{
    public enum State { Success, Failure, Uninitialized }
    private sealed record DomainError(string Message) : Error;
    private sealed record Payload(string Name);

    public static TheoryData<State, State, bool, bool> Cases
    {
        get
        {
            var cases = new TheoryData<State, State, bool, bool>();
            foreach (var source in Enum.GetValues<State>())
            foreach (var next in Enum.GetValues<State>())
            foreach (var typedSource in new[] { false, true })
            foreach (var typedCallback in new[] { false, true })
                cases.Add(source, next, typedSource, typedCallback);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Check_PreservesTheSourceValueOrFirstErrorAndExecutesOnlyOnceWhenApplicable(
        State sourceState, State nextState, bool typedSource, bool typedCallback)
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
        object Execute()
        {
            if (!typedSource)
                return typedCallback ? plain.Check(CheckValue) : plain.Check(CheckVoid);
            if (typedCallback)
                return valued.Check(value =>
                {
                    Assert.Same(originalValue, value);
                    return CheckValue();
                });
            return valued.Check(value =>
            {
                Assert.Same(originalValue, value);
                return CheckVoid();
            });
        }

        if (sourceState == State.Uninitialized ||
            (sourceState == State.Success && nextState == State.Uninitialized))
        {
            Assert.Throws<InvalidOperationException>(() => Execute());
            Assert.Equal(sourceState == State.Uninitialized ? 0 : 1, calls);
            return;
        }

        var result = Execute();
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
    public void Check_PropagatesTheOriginalCallbackException(bool typedSource, bool typedCallback)
    {
        var exception = new InvalidOperationException("callback");
        Rail CheckVoid() => throw exception;
        RailFor<int> CheckValue() => throw exception;
        var observed = Assert.Throws<InvalidOperationException>(() =>
        {
            if (typedSource)
            {
                if (typedCallback) Rail.Ok("original").Check(_ => CheckValue());
                else Rail.Ok("original").Check(_ => CheckVoid());
            }
            else
            {
                if (typedCallback) Rail.Ok().Check(CheckValue);
                else Rail.Ok().Check(CheckVoid);
            }
        });
        Assert.Same(exception, observed);
    }

    [Fact]
    public void Check_SupportsMethodGroupsAndStopsSubsequentStepsAfterFailure()
    {
        var original = new Payload("original");
        var error = new DomainError("rejected");
        var downstreamCalls = 0;
        Rail Validate(Payload value) { Assert.Same(original, value); return Rail.Ok(); }
        RailFor<int> Inspect(Payload value) { Assert.Same(original, value); return Rail.Ok(42); }

        RailFor<Payload> result = Rail.Ok(original)
            .Check(Validate)
            .Check(Inspect)
            .Check(_ => Rail.Fail(error))
            .Tap(_ => downstreamCalls++)
            .Bind(value => { downstreamCalls++; return Rail.Ok(value); });

        Assert.False(result.TryUnwrap(out var value, out var failure));
        Assert.Null(value);
        Assert.Same(error, failure);
        Assert.Equal(0, downstreamCalls);
    }
}
