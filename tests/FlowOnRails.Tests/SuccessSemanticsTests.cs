using FlowOnRails.Extensions.Tasks;

using Moq;

namespace FlowOnRails.Tests;

public class SuccessSemanticsTests
{
    private sealed record TestError(string Message) : Error;
    private sealed record TestSuccess(string Name) : Success;
    private sealed record OtherTestError(string Message) : Error;
    private sealed record OtherTestSuccess(string Name) : Success;

    [Fact]
    public void DefaultRail_ThrowsWhenObservedOrOperatedOn()
    {
        var rail = default(Rail);

        var propertyException = Assert.Throws<InvalidOperationException>(() => rail.IsSuccess);
        Assert.Contains("Rail.Ok", propertyException.Message);
        Assert.Throws<InvalidOperationException>(() => rail.TryUnwrap(out _));
        Assert.Throws<InvalidOperationException>(() => rail.Match(() => 1, _ => 0));
        Assert.Throws<InvalidOperationException>(() => rail.Map(() => 1));
        Assert.Throws<InvalidOperationException>(() => rail.Recover(_ => new TestSuccess("recovered")));
        Assert.Throws<InvalidOperationException>(() => rail.Equals(Rail.Ok()));
        Assert.Throws<InvalidOperationException>(() => rail.GetHashCode());
    }

    [Fact]
    public void DefaultRailFor_ThrowsWhenObservedOrOperatedOn()
    {
        var rail = default(RailFor<int>);

        var propertyException = Assert.Throws<InvalidOperationException>(() => rail.IsFailure);
        Assert.Contains("Rail.Fail", propertyException.Message);
        Assert.Throws<InvalidOperationException>(() => rail.TryUnwrap(out _, out _));
        Assert.Throws<InvalidOperationException>(() => rail.Match(value => value, _ => 0));
        Assert.Throws<InvalidOperationException>(() => rail.Bind(value => Rail.Ok(value + 1)));
        Assert.Throws<InvalidOperationException>(() => rail.Recover(_ => 42));
        Assert.Throws<InvalidOperationException>(() => rail.Equals(Rail.Ok(0)));
        Assert.Throws<InvalidOperationException>(() => rail.GetHashCode());
        Assert.Throws<InvalidOperationException>(() => ConvertToRail(rail));

        static Rail ConvertToRail(RailFor<int> source) => source;
    }

    [Fact]
    public void Outcome_ExposesTheActiveSemanticBranch()
    {
        var richSuccess = new TestSuccess("created");
        var error = new TestError("failed");
        var neutral = Rail.Ok();

        Assert.IsAssignableFrom<Success>(neutral.Outcome);
        Assert.Same(richSuccess, Rail.Succeed(richSuccess).Outcome);
        Assert.Same(richSuccess, Rail.Ok(42, richSuccess).Outcome);
        Assert.Same(error, Rail.Fail(error).Outcome);
        Assert.Same(error, Rail.Fail<int>(error).Outcome);
        Assert.Same(neutral.Outcome, Rail.Ok(42).Outcome);
    }

    [Fact]
    public void DefaultRail_ThrowsWhenOutcomeIsObserved()
    {
        Assert.Throws<InvalidOperationException>(() => default(Rail).Outcome);
        Assert.Throws<InvalidOperationException>(() => default(RailFor<int>).Outcome);
    }

    [Fact]
    public void NeutralSuccess_IsMaterializedAsSharedSingleton()
    {
        var first = Rail.Ok();
        var second = Rail.Ok();

        Assert.True(first.TryUnwrap(out var firstError, out var firstSuccess));
        Assert.True(second.TryUnwrap(out var secondError, out var secondSuccess));

        Assert.Null(firstError);
        Assert.Null(secondError);
        Assert.Same(firstSuccess, secondSuccess);
    }

    [Fact]
    public void RichFactories_ExposeProvidedSuccess()
    {
        var success = new TestSuccess("created");
        var rail = Rail.Ok(42, success);

        var isSuccess = rail.TryUnwrap(out var value, out var error, out var observedSuccess);

        Assert.True(isSuccess);
        Assert.Equal(42, value);
        Assert.Null(error);
        Assert.Same(success, observedSuccess);
    }

    [Fact]
    public void Factories_RejectNullOutcomes()
    {
        Assert.Throws<ArgumentNullException>(() => Rail.Succeed(null!));
        Assert.Throws<ArgumentNullException>(() => Rail.Ok(42, null!));
        Assert.Throws<ArgumentNullException>(() => Rail.Fail(null!));
        Assert.Throws<ArgumentNullException>(() => Rail.Fail<int>(null!));
    }

    [Fact]
    public async Task SucceedAsync_PreservesTheRichSuccess()
    {
        var success = new TestSuccess("created");

        var rail = await Rail.SucceedAsync(success);

        Assert.Same(success, rail.Outcome);
    }

    [Fact]
    public void RailForFactories_AreNotPartOfThePublicFacade()
    {
        var publicStaticMethods = typeof(RailFor<int>).GetMethods(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(publicStaticMethods, method => method.Name is "Ok" or "Fail");
    }

    [Fact]
    public void Bind_NeutralThenRich_UsesRichSuccess()
    {
        var success = new TestSuccess("rich");

        var result = Rail.Ok(1).Bind(value => Rail.Ok(value + 1, success));

        Assert.True(result.TryUnwrap(out var value, out _, out var observedSuccess));
        Assert.Equal(2, value);
        Assert.Same(success, observedSuccess);
    }

    [Fact]
    public void Bind_RichThenNeutral_PreservesRichSuccess()
    {
        var success = new TestSuccess("rich");

        var result = Rail.Ok(1, success).Bind(value => Rail.Ok(value + 1));

        Assert.True(result.TryUnwrap(out var value, out _, out var observedSuccess));
        Assert.Equal(2, value);
        Assert.Same(success, observedSuccess);
    }

    [Fact]
    public void Bind_RichThenRich_UsesLatestRichSuccess()
    {
        var first = new TestSuccess("first");
        var second = new TestSuccess("second");

        var result = Rail.Ok(1, first).Bind(value => Rail.Ok(value + 1, second));

        Assert.True(result.TryUnwrap(out _, out _, out var observedSuccess));
        Assert.Same(second, observedSuccess);
    }

    [Fact]
    public void Bind_WhenNextFails_DiscardsPreviousSuccess()
    {
        var success = new TestSuccess("previous");
        var error = new TestError("failed");

        var result = Rail.Ok(1, success).Bind<int>(_ => Rail.Fail<int>(error));

        Assert.False(result.TryUnwrap(out _, out var observedError, out var observedSuccess));
        Assert.Same(error, observedError);
        Assert.Null(observedSuccess);
    }

    [Fact]
    public void Ensure_WhenPredicateFails_DiscardsPreviousSuccess()
    {
        var success = new TestSuccess("previous");
        var error = new TestError("invalid");

        var result = Rail.Ok(1, success).Ensure(_ => false, error);

        Assert.False(result.TryUnwrap(out _, out var observedError, out var observedSuccess));
        Assert.Same(error, observedError);
        Assert.Null(observedSuccess);
    }

    [Fact]
    public void Bind_OnFailure_DoesNotExecuteCallback()
    {
        var error = new TestError("failed");
        var callback = new Mock<Func<int, RailFor<int>>>();

        var result = Rail.Fail<int>(error).Bind(callback.Object);

        callback.Verify(func => func(It.IsAny<int>()), Times.Never);
        Assert.False(result.TryUnwrap(out _, out var observedError, out _));
        Assert.Same(error, observedError);
    }

    [Fact]
    public void Bind_SatisfiesLeftIdentity()
    {
        var success = new TestSuccess("mapped");
        Func<int, RailFor<string>> bind = value => Rail.Ok(value.ToString(), success);

        Assert.Equal(bind(42), Rail.Ok(42).Bind(bind));
    }

    [Fact]
    public void Bind_SatisfiesRightIdentity()
    {
        var rail = Rail.Ok(42, new TestSuccess("original"));

        Assert.Equal(rail, rail.Bind(Rail.Ok));
    }

    [Fact]
    public void Bind_IsAssociative()
    {
        var initial = Rail.Ok(2, new TestSuccess("initial"));
        Func<int, RailFor<int>> first = value => Rail.Ok(value + 3, new TestSuccess("first"));
        Func<int, RailFor<string>> second = value => Rail.Ok((value * 2).ToString(), new TestSuccess("second"));

        var left = initial.Bind(first).Bind(second);
        var right = initial.Bind(value => first(value).Bind(second));

        Assert.Equal(left, right);
    }

    [Fact]
    public void Map_PreservesSuccessAndSatisfiesIdentity()
    {
        var rail = Rail.Ok(42, new TestSuccess("original"));

        var mapped = rail.Map(value => value);

        Assert.Equal(rail, mapped);
    }

    [Fact]
    public void Map_SatisfiesComposition()
    {
        var rail = Rail.Ok(5, new TestSuccess("original"));

        var sequential = rail.Map(value => value + 1).Map(value => value.ToString());
        var composed = rail.Map(value => (value + 1).ToString());

        Assert.Equal(sequential, composed);
    }

    [Fact]
    public void Match_RichOverload_ObservesSuccess()
    {
        var success = new TestSuccess("created");
        var rail = Rail.Ok(42, success);

        var observed = rail.Match(
            (value, outcome) => (value, outcome),
            _ => throw new Xunit.Sdk.XunitException("Failure branch should not execute."));

        Assert.Equal(42, observed.value);
        Assert.Same(success, observed.outcome);
    }

    [Fact]
    public void Recover_ProducesNeutralSuccessForRailFor()
    {
        var result = Rail.Fail<int>(new TestError("failed")).Recover(_ => 42);
        var neutral = Rail.Ok(0);

        Assert.True(result.TryUnwrap(out var value, out _, out var recoveredSuccess));
        Assert.True(neutral.TryUnwrap(out _, out _, out var neutralSuccess));
        Assert.Equal(42, value);
        Assert.Same(neutralSuccess, recoveredSuccess);
    }

    [Fact]
    public void Recover_OnSuccess_DoesNotExecuteCallback()
    {
        var success = new TestSuccess("original");
        var callback = new Mock<Func<Error, int>>();
        var rail = Rail.Ok(42, success);

        var result = rail.Recover(callback.Object);

        callback.Verify(func => func(It.IsAny<Error>()), Times.Never);
        Assert.Equal(rail, result);
    }

    [Fact]
    public void Recover_ProducesRichSuccessForRail()
    {
        var success = new TestSuccess("recovered");

        var result = Rail.Fail(new TestError("failed")).Recover(_ => success);

        Assert.True(result.TryUnwrap(out _, out var observedSuccess));
        Assert.Same(success, observedSuccess);
    }

    [Fact]
    public void RecoverWith_ReplacesFailureWithReturnedRail()
    {
        var success = new TestSuccess("fallback");

        var result = Rail.Fail<int>(new TestError("failed"))
            .RecoverWith(_ => Rail.Ok(42, success));

        Assert.True(result.TryUnwrap(out var value, out _, out var observedSuccess));
        Assert.Equal(42, value);
        Assert.Same(success, observedSuccess);
    }

    [Fact]
    public void ConversionToRail_PreservesSuccess()
    {
        var success = new TestSuccess("created");
        Rail result = Rail.Ok(42, success);

        Assert.True(result.TryUnwrap(out _, out var observedSuccess));
        Assert.Same(success, observedSuccess);
    }

    [Fact]
    public void Equality_IncludesSemanticSuccess()
    {
        var first = Rail.Ok(42, new TestSuccess("first"));
        var equal = Rail.Ok(42, new TestSuccess("first"));
        var different = Rail.Ok(42, new TestSuccess("second"));

        Assert.Equal(first, equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void UntypedEquality_IncludesSemanticSuccess()
    {
        var first = Rail.Succeed(new TestSuccess("first"));
        var equal = Rail.Succeed(new TestSuccess("first"));
        var different = Rail.Succeed(new TestSuccess("second"));

        Assert.Equal(first, equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(first, different);
    }

    [Fact]
    public void Equality_PreservesTheConcreteOutcomeType()
    {
        var success = Rail.Succeed(new TestSuccess("same"));
        var otherSuccessType = Rail.Succeed(new OtherTestSuccess("same"));
        var error = Rail.Fail(new TestError("same"));
        var otherErrorType = Rail.Fail(new OtherTestError("same"));

        Assert.NotEqual(success, otherSuccessType);
        Assert.NotEqual(error, otherErrorType);
    }

    [Fact]
    public void QuerySyntax_UsesBindAndMapSuccessSemantics()
    {
        var first = new TestSuccess("first");
        var second = new TestSuccess("second");

        var result =
            from left in Rail.Ok(1, first)
            from right in Rail.Ok(2, second)
            select left + right;

        Assert.True(result.TryUnwrap(out var value, out _, out var observedSuccess));
        Assert.Equal(3, value);
        Assert.Same(second, observedSuccess);
    }

    [Fact]
    public async Task TaskBindAsync_UsesCoreSuccessComposition()
    {
        var first = new TestSuccess("first");
        var second = new TestSuccess("second");
        var task = Task.FromResult(Rail.Ok(1, first));

        var result = await task.BindAsync(value => Task.FromResult(Rail.Ok(value + 1, second)));

        Assert.True(result.TryUnwrap(out _, out _, out var observedSuccess));
        Assert.Same(second, observedSuccess);
    }

    [Fact]
    public async Task ValueTaskBindAsync_UsesCoreSuccessComposition()
    {
        var first = new TestSuccess("first");
        var second = new TestSuccess("second");
        var task = ValueTask.FromResult(Rail.Ok(1, first));

        var result = await task.BindAsync(value => ValueTask.FromResult(Rail.Ok(value + 1, second)));

        Assert.True(result.TryUnwrap(out _, out _, out var observedSuccess));
        Assert.Same(second, observedSuccess);
    }
}
