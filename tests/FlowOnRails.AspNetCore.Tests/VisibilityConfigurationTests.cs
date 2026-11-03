using FlowOnRails.AspNetCore.Visibility;

namespace FlowOnRails.AspNetCore.Tests;

public class VisibilityConfigurationTests
{
    private abstract record PublicError : Error;
    private record NotFound : PublicError;
    private sealed record UserNotFound : NotFound;
    private sealed record Unrelated : Error;
    private sealed class UsersController;

    [Fact]
    public void UnfinishedSelections_AreRejected()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.For<NotFound>();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void ConflictingDuplicateSelections_AreRejectedInEitherOrder()
    {
        foreach (var reverse in new[] { false, true })
        {
            var builder = new ErrorVisibilityBuilder();
            if (reverse) builder.For<NotFound>().UseGenericProblem();
            builder.For<NotFound>().TranslateEachType();
            if (!reverse) builder.For<NotFound>().UseGenericProblem();
            Assert.Throws<InvalidOperationException>(() => builder.Build());
        }
    }

    [Fact]
    public void ReconfiguringTheSameSelection_RejectsContradictoryBehaviors()
    {
        var selection = new ErrorVisibilityBuilder().For<NotFound>();
        selection.UseGenericProblem();
        Assert.Throws<InvalidOperationException>(() => selection.TranslateEachType());
    }

    [Fact]
    public void IdenticalRegistrations_AreIdempotent()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.For<NotFound>().TranslateEachType();
        builder.For<NotFound>().TranslateEachType();
        Assert.Equal(new[] { typeof(NotFound) }, builder.Build().Resolve(typeof(NotFound)).TranslationCandidates);
    }

    [Fact]
    public void ExplicitIncludes_CannotReopenAGenericTreeWithinTheSameScope()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.ForTree<PublicError>().UseGenericProblem();
        builder.For<UserNotFound>().TranslateEachType();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void AncestorRules_CannotReopenAGenericDescendantRange()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.ForDescendantsOf<PublicError>().UseGenericProblem();
        builder.ForTree<NotFound>().UseNearestAncestorMapping();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void DescendantExclusion_StillAllowsItsOwnRoot()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.For<NotFound>().TranslateEachType();
        builder.ForDescendantsOf<NotFound>().UseGenericProblem();
        var policy = builder.Build();
        Assert.Equal(new[] { typeof(NotFound) }, policy.Resolve(typeof(NotFound)).TranslationCandidates);
        Assert.Empty(policy.Resolve(typeof(UserNotFound)).TranslationCandidates);
    }

    [Fact]
    public void DescendantsOnlyGenericRule_CanNarrowAnInclusionAtTheSameRoot()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.ForTree<NotFound>(tree =>
        {
            tree.TranslateEachType();
            tree.ForDescendantsOf<NotFound>().UseGenericProblem();
        });
        var policy = builder.Build();
        Assert.Equal(new[] { typeof(NotFound) }, policy.Resolve(typeof(NotFound)).TranslationCandidates);
        Assert.Empty(policy.Resolve(typeof(UserNotFound)).TranslationCandidates);
    }

    [Fact]
    public void TreeBlocks_RejectUnrelatedSelectionsAndCannotEscapeTheirRange()
    {
        var builder = new ErrorVisibilityBuilder();
        Assert.Throws<ArgumentException>(() => builder.ForTree<PublicError>(tree => tree.For<Unrelated>().TranslateEachType()));
        Assert.Throws<ArgumentException>(() => builder.ForDescendantsOf<PublicError>(tree => tree.For<PublicError>().TranslateEachType()));
        Assert.Throws<ArgumentException>(() => builder.ForDescendantsOf<PublicError>(tree => tree.ForTree<PublicError>().TranslateEachType()));
    }

    [Fact]
    public void OrganizationalBlocks_DoNotRequireARootBehavior()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.ForTree<PublicError>(tree => tree.For<NotFound>().TranslateEachType());
        Assert.Equal(new[] { typeof(NotFound) }, builder.Build().Resolve(typeof(NotFound)).TranslationCandidates);
    }

    [Fact]
    public void InvalidTypesAndNullCallbacks_AreRejected()
    {
        var builder = new ErrorVisibilityBuilder();
        Assert.Throws<ArgumentNullException>(() => builder.ForTree<PublicError>(null!));
        Assert.Throws<ArgumentNullException>(() => builder.ForDescendantsOf<PublicError>(null!));
        Assert.Throws<ArgumentNullException>(() => builder.ForController<UsersController>(null!));
        Assert.Throws<ArgumentException>(() => builder.ForController<IDisposable>(_ => { }));
        var policy = builder.Build();
        Assert.Throws<ArgumentNullException>(() => policy.Resolve(null!));
        Assert.Throws<ArgumentException>(() => policy.Resolve(typeof(string)));
        Assert.Throws<ArgumentException>(() => policy.Resolve(typeof(PublicError), typeof(IDisposable)));
        Assert.Throws<ArgumentException>(() => policy.Resolve(typeof(PublicError), typeof(List<>)));
    }
}
