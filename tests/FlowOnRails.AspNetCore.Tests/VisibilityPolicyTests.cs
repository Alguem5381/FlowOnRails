using FlowOnRails.AspNetCore.Visibility;

namespace FlowOnRails.AspNetCore.Tests;

public class VisibilityPolicyTests
{
    private abstract record PublicError(string Code) : Error;
    private record NotFoundError() : PublicError("not-found");
    private record UserNotFound() : NotFoundError;
    private sealed record DatabaseUserNotFound() : UserNotFound;
    private sealed record OtherPublicError() : PublicError("other");
    private sealed record UnrelatedError : Error;
    private sealed class UsersController;
    private sealed class AdminController;
    private class BaseController;
    private sealed class DerivedController : BaseController;

    [Fact]
    public void EmptyPolicy_UsesGenericResponseForUnknownErrorsInEveryScope()
    {
        var policy = new ErrorVisibilityBuilder().Build();
        foreach (var controller in new Type?[] { null, typeof(UsersController), typeof(AdminController) })
        {
            var result = policy.Resolve(typeof(DatabaseUserNotFound), controller);
            Assert.Equal(ErrorVisibilityBehavior.GenericProblem, result.Behavior);
            Assert.Empty(result.TranslationCandidates);
        }
    }

    public static TheoryData<int, int, ErrorVisibilityBehavior> RangeCases
    {
        get
        {
            var cases = new TheoryData<int, int, ErrorVisibilityBehavior>();
            for (var range = 0; range < 3; range++)
            for (var queried = 0; queried < 4; queried++)
            foreach (var behavior in Enum.GetValues<ErrorVisibilityBehavior>())
                cases.Add(range, queried, behavior);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(RangeCases))]
    public void Selections_DistinguishSelfDescendantsAndUnrelatedErrors(int range, int queried, ErrorVisibilityBehavior behavior)
    {
        var builder = new ErrorVisibilityBuilder();
        builder.For<PublicError>().TranslateEachType();
        var selection = range switch
        {
            0 => builder.For<NotFoundError>(),
            1 => builder.ForDescendantsOf<NotFoundError>(),
            _ => builder.ForTree<NotFoundError>()
        };
        switch (behavior)
        {
            case ErrorVisibilityBehavior.GenericProblem: selection.UseGenericProblem(); break;
            case ErrorVisibilityBehavior.TranslateEachType: selection.TranslateEachType(); break;
            default: selection.UseNearestAncestorMapping(); break;
        }
        Type[] types = [typeof(NotFoundError), typeof(UserNotFound), typeof(DatabaseUserNotFound), typeof(UnrelatedError)];
        bool[][] selected = [[true, false, false, false], [false, true, true, false], [true, true, true, false]];
        var result = builder.Build().Resolve(types[queried]);
        if (!selected[range][queried] || behavior == ErrorVisibilityBehavior.GenericProblem)
        {
            Assert.Equal(ErrorVisibilityBehavior.GenericProblem, result.Behavior);
            Assert.Empty(result.TranslationCandidates);
        }
        else
        {
            Assert.Equal(behavior, result.Behavior);
            Assert.Equal(new[] { behavior == ErrorVisibilityBehavior.TranslateEachType ? types[queried] : typeof(PublicError) }, result.TranslationCandidates);
        }
    }

    [Fact]
    public void FamilyInclusion_AuthorizesFutureDescendantsWithoutListingThem()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.ForTree<PublicError>().TranslateEachType();
        var result = builder.Build().Resolve(typeof(DatabaseUserNotFound));
        Assert.Equal(new[] { typeof(DatabaseUserNotFound) }, result.TranslationCandidates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoreSpecificRules_WinRegardlessOfRegistrationOrder(bool reverse)
    {
        var builder = new ErrorVisibilityBuilder();
        void General() => builder.ForTree<PublicError>().TranslateEachType();
        void Specific() => builder.For<UserNotFound>().UseGenericProblem();
        if (reverse) { Specific(); General(); }
        else { General(); Specific(); }
        var policy = builder.Build();
        Assert.Empty(policy.Resolve(typeof(UserNotFound)).TranslationCandidates);
        Assert.Equal(new[] { typeof(DatabaseUserNotFound) }, policy.Resolve(typeof(DatabaseUserNotFound)).TranslationCandidates);
        Assert.Equal(new[] { typeof(OtherPublicError) }, policy.Resolve(typeof(OtherPublicError)).TranslationCandidates);
    }

    [Fact]
    public void SameRoot_ExactThenDescendantsThenTreeDetermineSpecificity()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.ForTree<PublicError>().TranslateEachType();
        builder.ForDescendantsOf<PublicError>().UseNearestAncestorMapping();
        builder.For<PublicError>().TranslateEachType();
        var policy = builder.Build();
        Assert.Equal(new[] { typeof(PublicError) }, policy.Resolve(typeof(PublicError)).TranslationCandidates);
        Assert.Equal(new[] { typeof(PublicError) }, policy.Resolve(typeof(DatabaseUserNotFound)).TranslationCandidates);
    }

    [Fact]
    public void AncestorSearch_UsesAuthorizedContractsNearestFirstAndNeverConcreteType()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.ForTree<PublicError>().TranslateEachType();
        builder.For<DatabaseUserNotFound>().UseNearestAncestorMapping();
        var result = builder.Build().Resolve(typeof(DatabaseUserNotFound));
        Assert.Equal(ErrorVisibilityBehavior.NearestAncestorMapping, result.Behavior);
        Assert.Equal(new[] { typeof(UserNotFound), typeof(NotFoundError), typeof(PublicError) }, result.TranslationCandidates);
    }

    [Fact]
    public void AncestorSearch_SkipsUnconfiguredAncestorsAndFallsBackWhenNoneAreAuthorized()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.For<PublicError>().TranslateEachType();
        builder.For<DatabaseUserNotFound>().UseNearestAncestorMapping();
        Assert.Equal(new[] { typeof(PublicError) }, builder.Build().Resolve(typeof(DatabaseUserNotFound)).TranslationCandidates);
        var empty = new ErrorVisibilityBuilder();
        empty.ForTree<PublicError>().UseNearestAncestorMapping();
        Assert.Empty(empty.Build().Resolve(typeof(DatabaseUserNotFound)).TranslationCandidates);
    }

    [Fact]
    public void AncestorSearch_DoesNotEscapeAnExplicitGenericBoundary()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.For<PublicError>().TranslateEachType();
        builder.For<NotFoundError>().UseGenericProblem();
        builder.For<DatabaseUserNotFound>().UseNearestAncestorMapping();
        Assert.Empty(builder.Build().Resolve(typeof(DatabaseUserNotFound)).TranslationCandidates);
    }

    [Fact]
    public void AncestorTree_SearchesOutsideTheEntireSelectedSubtree()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.For<PublicError>().TranslateEachType();
        builder.ForTree<NotFoundError>().UseNearestAncestorMapping();
        var result = builder.Build().Resolve(typeof(DatabaseUserNotFound));
        Assert.Equal(new[] { typeof(PublicError) }, result.TranslationCandidates);
    }

    [Fact]
    public void AncestorTree_DoesNotProjectAContractInsideTheSelectedTree()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.For<PublicError>().TranslateEachType();
        builder.ForTree<NotFoundError>().UseNearestAncestorMapping();
        builder.For<UserNotFound>().TranslateEachType();
        var policy = builder.Build();
        Assert.Equal(new[] { typeof(UserNotFound) }, policy.Resolve(typeof(UserNotFound)).TranslationCandidates);
        Assert.Equal(new[] { typeof(PublicError) }, policy.Resolve(typeof(DatabaseUserNotFound)).TranslationCandidates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TreeBlocks_AndFlatRulesHaveIdenticalMeaning(bool nested)
    {
        var builder = new ErrorVisibilityBuilder();
        if (nested)
            builder.ForTree<PublicError>(tree =>
            {
                tree.TranslateEachType();
                tree.ForDescendantsOf<NotFoundError>(descendants => descendants.UseNearestAncestorMapping());
                tree.For<OtherPublicError>().UseGenericProblem();
            });
        else
        {
            builder.ForTree<PublicError>().TranslateEachType();
            builder.ForDescendantsOf<NotFoundError>().UseNearestAncestorMapping();
            builder.For<OtherPublicError>().UseGenericProblem();
        }
        var policy = builder.Build();
        Assert.Equal(new[] { typeof(NotFoundError), typeof(PublicError) }, policy.Resolve(typeof(DatabaseUserNotFound)).TranslationCandidates);
        Assert.Empty(policy.Resolve(typeof(OtherPublicError)).TranslationCandidates);
    }

    [Fact]
    public void ControllerRules_OverrideGlobalRulesWithoutAffectingOtherControllers()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.ForTree<PublicError>().TranslateEachType();
        builder.ForController<UsersController>(local => local.ForTree<NotFoundError>().UseGenericProblem());
        builder.ForController<AdminController>(local => local.For<DatabaseUserNotFound>().UseNearestAncestorMapping());
        var policy = builder.Build();
        Assert.Empty(policy.Resolve(typeof(DatabaseUserNotFound), typeof(UsersController)).TranslationCandidates);
        Assert.Equal(new[] { typeof(DatabaseUserNotFound) }, policy.Resolve(typeof(DatabaseUserNotFound)).TranslationCandidates);
        Assert.Equal(new[] { typeof(UserNotFound), typeof(NotFoundError), typeof(PublicError) }, policy.Resolve(typeof(DatabaseUserNotFound), typeof(AdminController)).TranslationCandidates);
        Assert.Equal(new[] { typeof(OtherPublicError) }, policy.Resolve(typeof(OtherPublicError), typeof(UsersController)).TranslationCandidates);
    }

    [Fact]
    public void ControllerScope_PrecedesEvenAMoreSpecificGlobalRule()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.For<DatabaseUserNotFound>().TranslateEachType();
        builder.ForController<UsersController>(local => local.ForTree<PublicError>().UseGenericProblem());
        Assert.Empty(builder.Build().Resolve(typeof(DatabaseUserNotFound), typeof(UsersController)).TranslationCandidates);
    }

    [Fact]
    public void ControllerOverride_CanExplicitlyAuthorizeAGloballyExcludedFamily()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.ForTree<PublicError>().UseGenericProblem();
        builder.ForController<AdminController>(local => local.ForTree<NotFoundError>().TranslateEachType());
        var policy = builder.Build();
        Assert.Equal(new[] { typeof(DatabaseUserNotFound) }, policy.Resolve(typeof(DatabaseUserNotFound), typeof(AdminController)).TranslationCandidates);
        Assert.Empty(policy.Resolve(typeof(DatabaseUserNotFound), typeof(UsersController)).TranslationCandidates);
    }

    [Fact]
    public void ControllerRules_AreExactAndNotInheritedByOtherControllers()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.ForController<BaseController>(local => local.ForTree<PublicError>().TranslateEachType());
        var policy = builder.Build();
        Assert.Equal(new[] { typeof(DatabaseUserNotFound) }, policy.Resolve(typeof(DatabaseUserNotFound), typeof(BaseController)).TranslationCandidates);
        Assert.Empty(policy.Resolve(typeof(DatabaseUserNotFound), typeof(DerivedController)).TranslationCandidates);
    }

    [Fact]
    public void AncestorSearch_UsesTheSameControllerScopeAndGenericBoundaries()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.ForTree<PublicError>().TranslateEachType();
        builder.For<DatabaseUserNotFound>().UseNearestAncestorMapping();
        builder.ForController<UsersController>(local => local.For<NotFoundError>().UseGenericProblem());
        Assert.Equal(new[] { typeof(UserNotFound) }, builder.Build().Resolve(typeof(DatabaseUserNotFound), typeof(UsersController)).TranslationCandidates);
    }

    [Fact]
    public void BuiltPolicies_AreSnapshotsAndCandidateListsCannotBeMutated()
    {
        var builder = new ErrorVisibilityBuilder();
        builder.ForTree<PublicError>().TranslateEachType();
        var policy = builder.Build();
        builder.For<UserNotFound>().UseGenericProblem();
        var result = policy.Resolve(typeof(UserNotFound));
        Assert.Equal(new[] { typeof(UserNotFound) }, result.TranslationCandidates);
        Assert.Throws<NotSupportedException>(() => ((IList<Type>)result.TranslationCandidates)[0] = typeof(Error));
        Assert.Empty(builder.Build().Resolve(typeof(UserNotFound)).TranslationCandidates);
    }
}
