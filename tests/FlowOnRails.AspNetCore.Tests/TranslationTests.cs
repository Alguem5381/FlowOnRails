using FlowOnRails.AspNetCore.Translation;
using FlowOnRails.AspNetCore.Visibility;
using Microsoft.AspNetCore.Http;

namespace FlowOnRails.AspNetCore.Tests;

public class TranslationTests
{
    private abstract record PublicError(string Resource) : Error;
    private record Missing(string Resource) : PublicError(Resource);
    private sealed record MissingUser(Guid Id) : Missing("user");
    private sealed record OptionalError(string? Reason) : Error;

    [Fact]
    public void HiddenError_DoesNotExecuteFactoriesOrProjectors()
    {
        var builder = new TranslationBuilder();
        builder.Add<MissingUser>(404,
            problem => problem.Detail(_ => throw new Exception("must not run")),
            (_, _) => throw new Exception("must not run"));

        var problem = builder.Build().Translate(new MissingUser(Guid.NewGuid()),
            new DefaultHttpContext(), new PolicyBuilder().Build());

        Assert.Equal(500, problem.Status);
        Assert.Equal("about:blank", problem.Type);
        Assert.Equal("Internal Server Error", problem.Title);
        Assert.Empty(problem.Extensions);
    }

    [Fact]
    public void AncestorSelection_ProjectsOnlyItsRegisteredContract()
    {
        var policy = new PolicyBuilder();
        policy.Global.For<Missing>().TranslateEachType();
        policy.Global.For<MissingUser>().UseNearestAncestorMapping();
        var builder = new TranslationBuilder();
        builder.Add<Missing>(404, p => p.Title("Missing"),
            (error, data) => data[nameof(error.Resource)] = error.Resource);
        builder.Add<MissingUser>(409, _ => { },
            (error, data) => data[nameof(error.Id)] = error.Id);

        var problem = builder.Build().Translate(new MissingUser(Guid.NewGuid()),
            new DefaultHttpContext(), policy.Build());

        Assert.Equal(404, problem.Status);
        Assert.Equal("user", problem.Extensions["Resource"]);
        Assert.False(problem.Extensions.ContainsKey("Id"));
    }

    [Fact]
    public void MissingCandidateMap_TriesNextAuthorizedAncestor()
    {
        var policy = new PolicyBuilder();
        policy.Global.For<PublicError>().TranslateEachType();
        policy.Global.For<Missing>().TranslateEachType();
        policy.Global.For<MissingUser>().UseNearestAncestorMapping();
        var builder = new TranslationBuilder();
        builder.Add<PublicError>(404, _ => { });

        var problem = builder.Build().Translate(new MissingUser(Guid.NewGuid()),
            new DefaultHttpContext(), policy.Build());

        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public void ExactTranslation_DoesNotApplyImplicitlyToDescendants()
    {
        var builder = new TranslationBuilder();
        builder.Add<Missing>(404, _ => { });
        var policy = Visible<MissingUser>();

        var problem = builder.Build().Translate(new MissingUser(Guid.NewGuid()),
            new DefaultHttpContext(), policy);

        Assert.Equal(500, problem.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticProjection_PreservesNullAndUsesFreshDictionaries(bool useData)
    {
        var builder = new TranslationBuilder();
        builder.Add<OptionalError>(422, _ => { },
            (error, data) => data[nameof(error.Reason)] = error.Reason, useData);
        var translator = builder.Build();
        var context = new DefaultHttpContext();
        var first = translator.Translate(new OptionalError(null), context, Visible<OptionalError>());
        var second = translator.Translate(new OptionalError("other"), context, Visible<OptionalError>());
        var firstData = useData ? Assert.IsType<Dictionary<string, object?>>(first.Extensions["data"]) : first.Extensions;
        var secondData = useData ? Assert.IsType<Dictionary<string, object?>>(second.Extensions["data"]) : second.Extensions;

        Assert.True(firstData.ContainsKey("Reason"));
        Assert.Null(firstData["Reason"]);
        Assert.Equal("other", secondData["Reason"]);
        Assert.NotSame(first, second);
        Assert.NotSame(firstData, secondData);
    }

    [Fact]
    public void GenericFactories_UseEachRequestWithoutAccessToTheError()
    {
        var calls = 0;
        var builder = new TranslationBuilder();
        builder.Generic(503, p =>
        {
            p.Type("https://example.test/unavailable");
            p.Title("Unavailable");
            p.Extensions((context, data) =>
            {
                calls++;
                data["traceId"] = context.TraceIdentifier;
            });
        });
        var translator = builder.Build();
        Assert.Equal(0, calls);
        var policy = new PolicyBuilder().Build();
        var first = translator.Translate(new MissingUser(Guid.NewGuid()),
            new DefaultHttpContext { TraceIdentifier = "first" }, policy);
        var second = translator.Translate(new MissingUser(Guid.NewGuid()),
            new DefaultHttpContext { TraceIdentifier = "second" }, policy);

        Assert.Equal(2, calls);
        Assert.Equal(503, first.Status);
        Assert.Equal("first", first.Extensions["traceId"]);
        Assert.Equal("second", second.Extensions["traceId"]);
    }

    [Fact]
    public void BuiltCatalog_DoesNotObserveLaterBuilderMutations()
    {
        ProblemBuilder<OptionalError>? captured = null;
        GenericProblemBuilder? generic = null;
        var builder = new TranslationBuilder();
        builder.Add<OptionalError>(422, p => { captured = p; p.Title("Before"); });
        builder.Generic(500, p => { generic = p; p.Title("Generic before"); });
        var translator = builder.Build();
        captured!.Title("After");
        generic!.Title("Generic after");
        builder.Add<Missing>(404, _ => { });

        Assert.Equal("Before", translator.Translate(new OptionalError(null),
            new DefaultHttpContext(), Visible<OptionalError>()).Title);
        Assert.Equal("Generic before", translator.Translate(new Missing("item"),
            new DefaultHttpContext(), Visible<Missing>()).Title);
    }

    [Fact]
    public void ManualProjection_AllowsApplicationChosenKeys()
    {
        var builder = new TranslationBuilder();
        builder.Add<OptionalError>(422, p => p.Extensions((_, data) => data["status"] = "custom"));

        var problem = builder.Build().Translate(new OptionalError(null),
            new DefaultHttpContext(), Visible<OptionalError>());

        Assert.Equal(422, problem.Status);
        Assert.Equal("custom", problem.Extensions["status"]);
    }

    [Fact]
    public void DuplicateMappingsAndMixedProjection_AreConfigurationErrors()
    {
        var builder = new TranslationBuilder();
        builder.Add<OptionalError>(422, _ => { });

        Assert.Throws<InvalidOperationException>(() => builder.Add<OptionalError>(400, _ => { }));
        Assert.Throws<InvalidOperationException>(() => new TranslationBuilder().Add<OptionalError>(422,
            p => p.Extensions((_, _) => { }), (_, _) => { }));
        Assert.Throws<InvalidOperationException>(() => new TranslationBuilder().Add<OptionalError>(422,
            _ => { }, useDataField: true));
        builder.Generic(500, _ => { });
        Assert.Throws<InvalidOperationException>(() => builder.Generic(503, _ => { }));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(399)]
    [InlineData(600)]
    public void InvalidErrorStatus_IsRejected(int status) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranslationBuilder().Add<OptionalError>(status, _ => { }));

    [Fact]
    public void CallbackExceptionsAndCancellation_AreNotDomainFallbacks()
    {
        var expected = new InvalidOperationException("getter failed");
        var builder = new TranslationBuilder();
        builder.Add<OptionalError>(422, p => p.Detail(_ => throw expected));
        var translator = builder.Build();
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            translator.Translate(new OptionalError(null), new DefaultHttpContext(), Visible<OptionalError>())));
        var context = new DefaultHttpContext { RequestAborted = new CancellationToken(true) };
        Assert.Throws<OperationCanceledException>(() => translator.Translate(new OptionalError(null), context, Visible<OptionalError>()));
    }

    private static ErrorPolicy Visible<TError>() where TError : Error
    {
        var builder = new PolicyBuilder();
        builder.Global.For<TError>().TranslateEachType();

        return builder.Build();
    }
}
