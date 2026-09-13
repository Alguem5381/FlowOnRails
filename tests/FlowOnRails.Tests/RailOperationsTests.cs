using Moq;

namespace FlowOnRails.Tests;

public class ResultOperationsTests
{
    private record TestError(string Message) : Error;

    [Fact]
    public void TryUnwrap_OnFailure_ReturnsFalseAndError()
    {
        var error = new TestError("Failure");
        var rail = Rail.Fail(error);

        var success = rail.TryUnwrap(out var outError);

        Assert.False(success);
        Assert.Equal(error, outError);
    }

    [Fact]
    public void TryUnwrap_OnSuccess_ReturnsTrueAndNull()
    {
        var rail = Rail.Ok();

        var success = rail.TryUnwrap(out var outError);

        Assert.True(success);
        Assert.Null(outError);
    }

    [Fact]
    public void Tap_OnSuccess_ExecutesAction()
    {
        var rail = Rail.Ok();
        var mockAction = new Mock<Action>();

        var returnedRail = rail.Tap(mockAction.Object);

        mockAction.Verify(a => a(), Times.Once);
        Assert.Equal(rail, returnedRail);
    }

    [Fact]
    public void Tap_OnFailure_DoesNotExecuteAction()
    {
        var rail = Rail.Fail(new TestError("Failure"));
        var mockAction = new Mock<Action>();

        rail.Tap(mockAction.Object);

        mockAction.Verify(a => a(), Times.Never);
    }

    [Fact]
    public void TapIf_ConditionTrue_OnSuccess_ExecutesAction()
    {
        var rail = Rail.Ok();
        var mockAction = new Mock<Action>();

        rail.TapIf(true, mockAction.Object);

        mockAction.Verify(a => a(), Times.Once);
    }

    [Fact]
    public void TapIf_ConditionFalse_OnSuccess_DoesNotExecuteAction()
    {
        var rail = Rail.Ok();
        var mockAction = new Mock<Action>();

        rail.TapIf(false, mockAction.Object);

        mockAction.Verify(a => a(), Times.Never);
    }

    [Fact]
    public void TapIf_PredicateTrue_OnSuccess_ExecutesAction()
    {
        var rail = Rail.Ok();
        var mockAction = new Mock<Action>();

        rail.TapIf(() => true, mockAction.Object);

        mockAction.Verify(a => a(), Times.Once);
    }

    [Fact]
    public void Recover_OnFailure_ExecutesFallback()
    {
        var error = new TestError("Failure");
        var rail = Rail.Fail(error);
        var recoveredRail = Rail.Ok();

        var fallback = new Mock<Func<Error, Rail>>();
        fallback.Setup(f => f(error)).Returns(recoveredRail);

        var finalRail = rail.Recover(fallback.Object);

        fallback.Verify(f => f(error), Times.Once);
        Assert.Equal(recoveredRail, finalRail);
    }

    [Fact]
    public void Recover_OnSuccess_DoesNotExecuteFallback()
    {
        var rail = Rail.Ok();
        var fallback = new Mock<Func<Error, Rail>>();

        var finalRail = rail.Recover(fallback.Object);

        fallback.Verify(f => f(It.IsAny<Error>()), Times.Never);
        Assert.Equal(rail, finalRail);
    }

    [Fact]
    public void Bind_OnSuccess_ExecutesBindFunction()
    {
        var rail = Rail.Ok();
        var nextRail = Rail.Ok();
        var bind = new Mock<Func<Rail>>();
        bind.Setup(b => b()).Returns(nextRail);

        var finalRail = rail.Bind(bind.Object);

        bind.Verify(b => b(), Times.Once);
        Assert.Equal(nextRail, finalRail);
    }

    [Fact]
    public void Bind_OnFailure_DoesNotExecuteBindFunctionAndReturnsFailure()
    {
        var error = new TestError("Failure");
        var rail = Rail.Fail(error);
        var bind = new Mock<Func<Rail>>();

        var finalRail = rail.Bind(bind.Object);

        bind.Verify(b => b(), Times.Never);
        Assert.True(finalRail.IsFailure);
        Assert.Equal(error, finalRail.Error);
    }



    [Fact]
    public void Ensure_OnSuccessAndPredicateTrue_ReturnsSuccess()
    {
        var rail = Rail.Ok();
        var error = new TestError("Ensure failure");

        var finalRail = rail.Ensure(() => true, error);

        Assert.True(finalRail.IsSuccess);
    }

    [Fact]
    public void Ensure_OnSuccessAndPredicateFalse_ReturnsFailure()
    {
        var rail = Rail.Ok();
        var error = new TestError("Ensure failure");

        var finalRail = rail.Ensure(() => false, error);

        Assert.True(finalRail.IsFailure);
        Assert.Equal(error, finalRail.Error);
    }

    [Fact]
    public void Ensure_OnFailure_DoesNotEvaluatePredicate()
    {
        var originalError = new TestError("Original");
        var rail = Rail.Fail(originalError);
        var ensureError = new TestError("Ensure failure");
        var predicate = new Mock<Func<bool>>();

        var finalRail = rail.Ensure(predicate.Object, ensureError);

        predicate.Verify(p => p(), Times.Never);
        Assert.True(finalRail.IsFailure);
        Assert.Equal(originalError, finalRail.Error);
    }

    [Fact]
    public void Match_OnSuccess_ExecutesOnSuccess()
    {
        var rail = Rail.Ok();
        var onSuccess = new Mock<Func<string>>();
        onSuccess.Setup(f => f()).Returns("Success");
        var onFailure = new Mock<Func<Error, string>>();

        var matchRail = rail.Match(onSuccess.Object, onFailure.Object);

        onSuccess.Verify(f => f(), Times.Once);
        onFailure.Verify(f => f(It.IsAny<Error>()), Times.Never);
        Assert.Equal("Success", matchRail);
    }

    [Fact]
    public void Match_OnFailure_ExecutesOnFailure()
    {
        var error = new TestError("Failure");
        var rail = Rail.Fail(error);
        var onSuccess = new Mock<Func<string>>();
        var onFailure = new Mock<Func<Error, string>>();
        onFailure.Setup(f => f(error)).Returns("Failure String");

        var matchRail = rail.Match(onSuccess.Object, onFailure.Object);

        onSuccess.Verify(f => f(), Times.Never);
        onFailure.Verify(f => f(error), Times.Once);
        Assert.Equal("Failure String", matchRail);
    }

    [Fact]
    public void Bind_ToRailFor_OnSuccess_ReturnsRailFor()
    {
        var rail = Rail.Ok();
        var nextRail = RailFor<int>.Ok(42);
        
        var finalRail = rail.Bind(() => nextRail);
        
        Assert.True(finalRail.IsSuccess);
        Assert.Equal(42, finalRail.Value);
    }

    [Fact]
    public void Map_ToValue_OnSuccess_ReturnsRailFor()
    {
        var rail = Rail.Ok();
        
        var finalRail = rail.Map(() => 42);
        
        Assert.True(finalRail.IsSuccess);
        Assert.Equal(42, finalRail.Value);
    }

    [Fact]
    public void MapError_OnFailure_ReturnsMappedError()
    {
        var error = new TestError("Original");
        var rail = Rail.Fail(error);
        
        var finalRail = rail.MapError(e => new TestError($"Mapped: {((TestError)e).Message}"));
        
        Assert.True(finalRail.IsFailure);
        Assert.Equal("Mapped: Original", ((TestError)finalRail.Error!).Message);
    }
}
