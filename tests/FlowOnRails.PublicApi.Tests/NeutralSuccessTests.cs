namespace FlowOnRails.PublicApi.Tests;

public class NeutralSuccessTests
{
    private sealed record DomainSuccess(string Name) : Success;

    private sealed record SuccessGroup(IReadOnlyList<Success> Items) : Success;

    private sealed record DomainError : Error;

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(5, true)]
    public void ObservedNeutral_RoundTripsThroughFactories(int observation, bool clone)
    {
        var observed = ObserveNeutral(observation);
        var input = clone ? observed with { } : observed;
        if (clone)
            Assert.NotSame(observed, input);

        var untyped = Rail.Succeed(input);
        var typed = Rail.Ok("value", input);
        Rail converted = typed;

        Assert.Equal(Rail.Ok(), untyped);
        Assert.Equal(Rail.Ok().GetHashCode(), untyped.GetHashCode());
        Assert.Equal(Rail.Ok("value"), typed);
        Assert.Equal(Rail.Ok("value").GetHashCode(), typed.GetHashCode());
        Assert.Equal(Rail.Ok(), converted);
        Assert.Same(observed, untyped.Outcome);
        Assert.Same(observed, typed.Outcome);
        Assert.Same(observed, converted.Outcome);
        Assert.Single(new HashSet<Rail> { Rail.Ok(), untyped, converted });
        Assert.Single(new HashSet<RailFor<string>> { Rail.Ok("value"), typed });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bind_WithObservedNeutral_PreservesRichSuccessAcrossValueTransitions(bool clone)
    {
        var observed = (Success)Rail.Ok().Outcome;
        var neutral = clone ? observed with { } : observed;
        var rich = new DomainSuccess("created");

        Assert.Equal(Rail.Succeed(rich),
            Rail.Succeed(rich).Bind(() => Rail.Succeed(neutral)));
        Assert.Equal(Rail.Ok(2, rich),
            Rail.Succeed(rich).Bind(() => Rail.Ok(2, neutral)));
        Assert.Equal(Rail.Succeed(rich),
            Rail.Ok(1, rich).Bind(_ => Rail.Succeed(neutral)));
        Assert.Equal(Rail.Ok(2, rich),
            Rail.Ok(1, rich).Bind(value => Rail.Ok(value + 1, neutral)));
    }

    [Fact]
    public async Task AsyncFactories_NormalizeCopiedNeutral()
    {
        var neutral = ((Success)Rail.Ok().Outcome) with { };

        Assert.Equal(Rail.Ok(), await Rail.SucceedAsync(neutral));
        Assert.Equal(Rail.Ok(42), await Rail.OkAsync(42, neutral));
    }

    [Fact]
    public void Recovery_WithCopiedNeutral_RestoresCanonicalSuccess()
    {
        var neutral = ((Success)Rail.Ok().Outcome) with { };
        var failure = Rail.Fail(new DomainError());

        Assert.Equal(Rail.Ok(), failure.Recover(_ => neutral));
        Assert.Equal(Rail.Ok(), failure.RecoverWith(_ => Rail.Succeed(neutral)));
        Assert.Equal(Rail.Ok(42), Rail.Fail<int>(new DomainError())
            .RecoverWith(_ => Rail.Ok(42, neutral)));
    }

    [Fact]
    public void EmptyDomainAggregate_IsRichAndReplacesPreviousSuccess()
    {
        var previous = new DomainSuccess("previous");
        var aggregate = new SuccessGroup(Array.Empty<Success>());

        var result = Rail.Ok(1, previous).Bind(value => Rail.Ok(value + 1, aggregate));

        Assert.Same(aggregate, result.Outcome);
        Assert.Equal(Rail.Ok(2, aggregate), result);
        Assert.NotEqual(Rail.Ok(2), result);
    }

    [Fact]
    public void DomainAggregate_IsPreservedByNeutralAndReplacedByRichSuccess()
    {
        var item = new DomainSuccess("original");
        var aggregate = new SuccessGroup([item]);
        var next = new DomainSuccess("next");
        var source = Rail.Ok(1, aggregate);
        var neutral = ((Success)Rail.Ok().Outcome) with { };

        Assert.Same(aggregate, source.Bind(value => Rail.Ok(value + 1, neutral)).Outcome);
        Assert.Same(next, source.Bind(value => Rail.Ok(value + 1, next)).Outcome);
        Assert.Same(item, Assert.Single(aggregate.Items));
    }

    [Fact]
    public void BindLaws_HoldAcrossRichNeutralReconstructedAndFailedResults()
    {
        var neutral = ((Success)Rail.Ok().Outcome) with { };
        var rich = new DomainSuccess("rich");
        RailFor<int>[] sources =
        [
            Rail.Ok(1), Rail.Ok(1, neutral), Rail.Ok(1, rich), Rail.Fail<int>(new DomainError())
        ];
        Func<int, RailFor<int>>[] steps =
        [
            value => Rail.Ok(value + 1),
            value => Rail.Ok(value + 1, neutral),
            value => Rail.Ok(value + 1, rich),
            _ => Rail.Fail<int>(new DomainError())
        ];

        foreach (var step in steps)
            Assert.Equal(step(1), Rail.Ok(1).Bind(step));

        foreach (var source in sources)
        {
            Assert.Equal(source, source.Bind(Rail.Ok));
            Assert.Equal(source, source.Map(value => value));
            foreach (var first in steps)
            foreach (var second in steps)
                Assert.Equal(source.Bind(first).Bind(second),
                    source.Bind(value => first(value).Bind(second)));
        }
    }

    private static Success ObserveNeutral(int observation)
    {
        switch (observation)
        {
            case 0:
                return (Success)Rail.Ok().Outcome;
            case 1:
                return Rail.Ok().Match(success => success, _ => throw new InvalidOperationException());
            case 2:
                if (Rail.Ok().TryUnwrap(out _, out var untypedSuccess))
                    return untypedSuccess;
                break;
            case 3:
                return (Success)Rail.Ok(42).Outcome;
            case 4:
                return Rail.Ok(42).Match((_, success) => success, _ => throw new InvalidOperationException());
            case 5:
                if (Rail.Ok(42).TryUnwrap(out _, out _, out var typedSuccess))
                    return typedSuccess;
                break;
        }

        throw new InvalidOperationException("Expected an observable neutral success.");
    }
}
