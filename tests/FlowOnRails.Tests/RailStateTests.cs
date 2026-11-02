namespace FlowOnRails.Tests;

public class RailStateTests
{
    private sealed record DomainError(string Message) : Error;
    private sealed record OtherError(string Message) : Error;

    [Fact]
    public void DefaultRails_RejectObservationAndCompositionBeforeInvokingCallbacks()
    {
        var plain = default(Rail);
        var valued = default(RailFor<int>);
        var calls = 0;
        Assert.Throws<InvalidOperationException>(() => plain.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => plain.IsFailure);
        Assert.Throws<InvalidOperationException>(() => valued.IsSuccess);
        Assert.Throws<InvalidOperationException>(() => valued.IsFailure);
        Assert.Throws<InvalidOperationException>(() => plain.TryUnwrap(out _));
        Assert.Throws<InvalidOperationException>(() => valued.TryUnwrap(out _, out _));
        Assert.Throws<InvalidOperationException>(() => plain.Map(() => ++calls));
        Assert.Throws<InvalidOperationException>(() => valued.Recover(_ => ++calls));
        Assert.Throws<InvalidOperationException>(() => plain.RecoverWith(_ => { calls++; return Rail.Ok(); }));
        Assert.Throws<InvalidOperationException>(() => { Rail ignored = valued; });
        Assert.Throws<InvalidOperationException>(() => plain.GetHashCode());
        Assert.Throws<InvalidOperationException>(() => valued.GetHashCode());
        Assert.Throws<InvalidOperationException>(() => plain.Equals(Rail.Ok()));
        Assert.Throws<InvalidOperationException>(() => Rail.Ok(1).Equals(valued));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void FactoriesAndConversions_RejectNullValuesAndErrors()
    {
        Assert.Throws<ArgumentNullException>(() => Rail.Ok<string>(null!));
        Assert.Throws<ArgumentNullException>(() => Rail.Fail(null!));
        Assert.Throws<ArgumentNullException>(() => Rail.Fail<int>(null!));
        Assert.Throws<ArgumentNullException>(() => { RailFor<string> ignored = (string)null!; });
        Assert.Throws<ArgumentNullException>(() => { RailFor<int> ignored = (Error)null!; });
        Assert.Throws<ArgumentNullException>(() => { Rail ignored = (Error)null!; });
    }

    [Fact]
    public void BindAndRecovery_RejectUninitializedCallbackResults()
    {
        var error = new DomainError("failed");
        Assert.Throws<InvalidOperationException>(() => Rail.Ok().Bind(() => default(Rail)));
        Assert.Throws<InvalidOperationException>(() => Rail.Ok().Bind(() => default(RailFor<int>)));
        Assert.Throws<InvalidOperationException>(() => Rail.Ok(1).Bind(_ => default(Rail)));
        Assert.Throws<InvalidOperationException>(() => Rail.Ok(1).Bind(_ => default(RailFor<int>)));
        Assert.Throws<InvalidOperationException>(() => Rail.Fail(error).RecoverWith(_ => default));
        Assert.Throws<InvalidOperationException>(() => Rail.Fail<int>(error).RecoverWith(_ => default));
    }

    [Fact]
    public void EqualityAndHashing_UseValuesAndConcreteErrorTypes()
    {
        var error = new DomainError("same");
        var equal = new DomainError("same");
        var differentType = new OtherError("same");
        Assert.Equal(Rail.Fail(error), Rail.Fail(equal));
        Assert.Equal(Rail.Fail(error).GetHashCode(), Rail.Fail(equal).GetHashCode());
        Assert.Equal(Rail.Fail<int>(error), Rail.Fail<int>(equal));
        Assert.Equal(Rail.Fail<int>(error).GetHashCode(), Rail.Fail<int>(equal).GetHashCode());
        Assert.NotEqual(Rail.Fail(error), Rail.Fail(differentType));
        Assert.NotEqual(Rail.Fail<int>(error), Rail.Fail<int>(differentType));
        Assert.Equal(Rail.Ok(42), Rail.Ok(42));
        Assert.Equal(Rail.Ok(42).GetHashCode(), Rail.Ok(42).GetHashCode());
        Assert.NotEqual(Rail.Ok(42), Rail.Ok(7));
        Assert.NotEqual(Rail.Ok(), Rail.Fail(error));
    }
}
