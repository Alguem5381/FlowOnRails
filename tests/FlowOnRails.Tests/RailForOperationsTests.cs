using Moq;

namespace FlowOnRails.Tests;

public class ResultOfTOperationsTests
{
    private record TestError(string Message) : Error;



    [Fact]
    public void TryUnwrap_OnSuccess_ReturnsTrueAndValue()
    {
        var rail = Rail.Ok("Value");

        var success = rail.TryUnwrap(out var outValue, out var outError);

        Assert.True(success);
        Assert.Equal("Value", outValue);
        Assert.Null(outError);
    }

    [Fact]
    public void TryUnwrap_OnFailure_ReturnsFalseAndError()
    {
        var error = new TestError("Failure");
        var rail = Rail.Fail<string>(error);

        var success = rail.TryUnwrap(out var outValue, out var outError);

        Assert.False(success);
        Assert.Null(outValue); // default!
        Assert.Equal(error, outError);
    }

    [Fact]
    public void Ensure_OnSuccessAndPredicateTrue_ReturnsSuccess()
    {
        var rail = RailFor<string>.Ok("Value");
        var error = new TestError("Ensure error");

        var finalRail = rail.Ensure(v => v == "Value", error);

        Assert.True(finalRail.IsSuccess);
    }

    [Fact]
    public void Ensure_OnSuccessAndPredicateFalse_ReturnsFailure()
    {
        var rail = RailFor<string>.Ok("Value");
        var error = new TestError("Ensure error");

        var finalRail = rail.Ensure(v => v != "Value", error);

        Assert.True(finalRail.IsFailure);
        Assert.Equal(error, finalRail.Error);
    }

    [Fact]
    public void Ensure_OnFailure_DoesNotEvaluatePredicate()
    {
        var originalError = new TestError("Original");
        var rail = RailFor<string>.Fail(originalError);
        var error = new TestError("Ensure error");
        var predicate = new Mock<Func<string, bool>>();

        var finalRail = rail.Ensure(predicate.Object, error);

        predicate.Verify(p => p(It.IsAny<string>()), Times.Never);
        Assert.True(finalRail.IsFailure);
        Assert.Equal(originalError, finalRail.Error);
    }

    [Fact]
    public void Tap_OnSuccess_ExecutesActionWithValue()
    {
        var rail = RailFor<string>.Ok("Value");
        var mockAction = new Mock<Action<string>>();

        var returnedRail = rail.Tap(mockAction.Object);

        mockAction.Verify(a => a("Value"), Times.Once);
        Assert.Equal(rail, returnedRail);
    }

    [Fact]
    public void Tap_OnFailure_DoesNotExecuteAction()
    {
        var rail = RailFor<string>.Fail(new TestError("Error"));
        var mockAction = new Mock<Action<string>>();

        rail.Tap(mockAction.Object);

        mockAction.Verify(a => a(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void TapIf_ConditionTrue_OnSuccess_ExecutesAction()
    {
        var rail = RailFor<string>.Ok("Value");
        var mockAction = new Mock<Action<string>>();

        rail.TapIf(true, mockAction.Object);

        mockAction.Verify(a => a("Value"), Times.Once);
    }

    [Fact]
    public void TapIf_PredicateTrue_OnSuccess_ExecutesAction()
    {
        var rail = RailFor<string>.Ok("Value");
        var mockAction = new Mock<Action<string>>();

        rail.TapIf(v => v == "Value", mockAction.Object);

        mockAction.Verify(a => a("Value"), Times.Once);
    }

    [Fact]
    public void TapIf_PredicateFalse_OnSuccess_DoesNotExecuteAction()
    {
        var rail = RailFor<string>.Ok("Value");
        var mockAction = new Mock<Action<string>>();

        rail.TapIf(v => v != "Value", mockAction.Object);

        mockAction.Verify(a => a(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Recover_OnFailure_ExecutesFallback()
    {
        var error = new TestError("Failure");
        var rail = RailFor<string>.Fail(error);
        var recoveredRail = RailFor<string>.Ok("Recovered");

        var fallback = new Mock<Func<Error, RailFor<string>>>();
        fallback.Setup(f => f(error)).Returns(recoveredRail);

        var finalRail = rail.Recover(fallback.Object);

        fallback.Verify(f => f(error), Times.Once);
        Assert.Equal(recoveredRail, finalRail);
    }

    [Fact]
    public void Recover_OnSuccess_DoesNotExecuteFallback()
    {
        var rail = RailFor<string>.Ok("Value");
        var fallback = new Mock<Func<Error, RailFor<string>>>();

        var finalRail = rail.Recover(fallback.Object);

        fallback.Verify(f => f(It.IsAny<Error>()), Times.Never);
        Assert.Equal(rail, finalRail);
    }

    [Fact]
    public void Match_OnSuccess_ExecutesOnSuccess()
    {
        var rail = RailFor<string>.Ok("Value");
        var onSuccess = new Mock<Func<string, int>>();
        onSuccess.Setup(f => f("Value")).Returns(1);
        var onFailure = new Mock<Func<Error, int>>();

        var matchRail = rail.Match(onSuccess.Object, onFailure.Object);

        onSuccess.Verify(f => f("Value"), Times.Once);
        onFailure.Verify(f => f(It.IsAny<Error>()), Times.Never);
        Assert.Equal(1, matchRail);
    }

    [Fact]
    public void Match_OnFailure_ExecutesOnFailure()
    {
        var error = new TestError("Failure");
        var rail = RailFor<string>.Fail(error);
        var onSuccess = new Mock<Func<string, int>>();
        var onFailure = new Mock<Func<Error, int>>();
        onFailure.Setup(f => f(error)).Returns(0);

        var matchRail = rail.Match(onSuccess.Object, onFailure.Object);

        onSuccess.Verify(f => f(It.IsAny<string>()), Times.Never);
        onFailure.Verify(f => f(error), Times.Once);
        Assert.Equal(0, matchRail);
    }

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        var rail = RailFor<string>.Ok("123");
        var map = new Mock<Func<string, int>>();
        map.Setup(m => m("123")).Returns(123);

        var finalRail = rail.Map(map.Object);

        map.Verify(m => m("123"), Times.Once);
        Assert.True(finalRail.IsSuccess);
        Assert.Equal(123, finalRail.Value);
    }

    [Fact]
    public void Map_OnFailure_ReturnsFailureAndDoesNotMap()
    {
        var error = new TestError("Failure");
        var rail = RailFor<string>.Fail(error);
        var map = new Mock<Func<string, int>>();

        var finalRail = rail.Map(map.Object);

        map.Verify(m => m(It.IsAny<string>()), Times.Never);
        Assert.True(finalRail.IsFailure);
        Assert.Equal(error, finalRail.Error);
    }

    [Fact]
    public void Bind_OnSuccess_ExecutesBindFunction()
    {
        var rail = RailFor<string>.Ok("123");
        var nextRail = RailFor<int>.Ok(123);
        var bind = new Mock<Func<string, RailFor<int>>>();
        bind.Setup(b => b("123")).Returns(nextRail);

        var finalRail = rail.Bind(bind.Object);

        bind.Verify(b => b("123"), Times.Once);
        Assert.Equal(nextRail, finalRail);
    }

    [Fact]
    public void Bind_OnFailure_DoesNotExecuteBindFunctionAndReturnsFailure()
    {
        var error = new TestError("Failure");
        var rail = RailFor<string>.Fail(error);
        var bind = new Mock<Func<string, RailFor<int>>>();

        var finalRail = rail.Bind(bind.Object);

        bind.Verify(b => b(It.IsAny<string>()), Times.Never);
        Assert.True(finalRail.IsFailure);
        Assert.Equal(error, finalRail.Error);
    }

    [Fact]
    public void Bind_ToRail_OnSuccess_ReturnsRail()
    {
        var rail = RailFor<string>.Ok("Value");
        
        var finalRail = rail.Bind(v => Rail.Ok());
        
        Assert.True(finalRail.IsSuccess);
    }

    [Fact]
    public void MapError_OnFailure_ReturnsMappedError()
    {
        var error = new TestError("Original");
        var rail = RailFor<string>.Fail(error);
        
        var finalRail = rail.MapError(e => new TestError($"Mapped: {((TestError)e).Message}"));
        
        Assert.True(finalRail.IsFailure);
        Assert.Equal("Mapped: Original", ((TestError)finalRail.Error!).Message);
    }
}
