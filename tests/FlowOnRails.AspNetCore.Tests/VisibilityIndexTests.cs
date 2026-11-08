using FlowOnRails.AspNetCore.Visibility;

namespace FlowOnRails.AspNetCore.Tests;

public class VisibilityIndexTests
{
    private abstract record PublicError : Error;
    private record FamilyError : PublicError;
    private sealed record LeafError : FamilyError;
    private sealed record TaggedError<TFirst, TSecond> : Error;
    private sealed class UsersController;
    private sealed class OtherController;

    [Fact]
    public void UnrelatedRules_DoNotChangeGlobalOrControllerResolution()
    {
        var builder = new PolicyBuilder();
        builder.Global.For<PublicError>().TranslateEachType();
        builder.Global.ForDescendantsOf<PublicError>().UseNearestAncestorMapping();
        builder.ForController<UsersController>(scope => scope.ForTree<PublicError>().UseGenericProblem());
        AddUnrelatedRules<int>(builder.Global);
        AddUnrelatedRules<string>(builder.Global);
        builder.ForController<UsersController>(AddUnrelatedRules<double>);
        builder.ForController<OtherController>(AddUnrelatedRules<Guid>);
        var policy = builder.Build();

        Assert.Equal(new[] { typeof(PublicError) }, policy.Resolve(typeof(LeafError)).TranslationCandidates);
        Assert.Empty(policy.Resolve(typeof(LeafError), typeof(UsersController)).TranslationCandidates);
        Assert.Equal(new[] { typeof(PublicError) }, policy.Resolve(typeof(LeafError), typeof(OtherController)).TranslationCandidates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedRangePriority_DoesNotDependOnRegistrationOrder(bool reverse)
    {
        var builder = new PolicyBuilder();
        builder.Global.For<PublicError>().TranslateEachType();

        void Broad() => builder.Global.ForTree<FamilyError>().UseNearestAncestorMapping();
        void Descendants() => builder.Global.ForDescendantsOf<FamilyError>().UseGenericProblem();
        void Exact() => builder.Global.For<FamilyError>().TranslateEachType();

        if (reverse)
        {
            Exact();
            Descendants();
            Broad();
        }
        else
        {
            Broad();
            Descendants();
            Exact();
        }

        var policy = builder.Build();

        Assert.Equal(new[] { typeof(FamilyError) }, policy.Resolve(typeof(FamilyError)).TranslationCandidates);
        Assert.Empty(policy.Resolve(typeof(LeafError)).TranslationCandidates);
    }

    [Fact]
    public async Task IndexedPolicy_IsSafeToShareAcrossConcurrentResolutions()
    {
        var builder = new PolicyBuilder();
        builder.Global.ForTree<PublicError>().TranslateEachType();
        builder.ForController<UsersController>(scope => scope.For<LeafError>().UseGenericProblem());
        var policy = builder.Build();
        var tasks = Enumerable.Range(0, 128).Select(index => Task.Run(() =>
        {
            var controller = index % 2 == 0 ? typeof(UsersController) : typeof(OtherController);
            var result = policy.Resolve(typeof(LeafError), controller);

            Assert.Equal(index % 2 == 0 ? Array.Empty<Type>() : new[] { typeof(LeafError) }, result.TranslationCandidates);
        }));

        await Task.WhenAll(tasks);
    }

    private static void AddUnrelatedRules<T>(RuleScope scope)
    {
        scope.For<TaggedError<T, int>>().TranslateEachType();
        scope.For<TaggedError<T, long>>().UseGenericProblem();
        scope.For<TaggedError<T, string>>().UseNearestAncestorMapping();
        scope.For<TaggedError<T, Guid>>().TranslateEachType();
    }
}
