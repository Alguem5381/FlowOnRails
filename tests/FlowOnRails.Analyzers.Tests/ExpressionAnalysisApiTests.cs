using FlowOnRails.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    [Fact]
    public void ExpressionApi_QueriesBeforeAndAfterRecoveryInsideAnUntrackedReturnBody()
    {
        var compilation = ExpressionCompilation("""
            using FlowOnRails;
            public sealed record Missing : Error;
            public static class Api
            {
                static void Observe(RailFor<int> value) { }
                public static string Controller()
                {
                    var result = Rail.Fail<int>(new Missing());
                    Observe(result);
                    result = result.Recover(error => 42);
                    Observe(result);
                    return "response";
                }
            }
            """);
        var analysis = new ErrorFlowAnalysis(compilation);
        var method = ApiMethod(compilation, "Api", "Controller");
        var expressions = ObservedArguments(compilation);
        var results = analysis.AnalyzeExpressions(method, expressions);

        Assert.False(analysis.CanAnalyze(method));
        Assert.Equal(2, results.Length);
        Assert.Same(expressions[0], results[0].Expression);
        AssertExpressionErrors(results[0], "Missing");
        AssertExpressionErrors(results[1]);
        Assert.True(results[1].Summary!.MaySucceed);
        Assert.False(results[1].Summary!.MayBeInvalid);
    }

    [Fact]
    public void ExpressionApi_SpecializesEachCallWithoutMixingUnknownAlternatives()
    {
        var compilation = ExpressionCompilation("""
            using FlowOnRails;
            public sealed record First : Error;
            public sealed record Second : Error;
            public interface IReader { RailFor<int> Read(); }
            public static class Api
            {
                static RailFor<int> Choose(bool first) => first ? new First() : new Second();
                public static void Run(bool flag, IReader reader)
                {
                    var unknown = reader.Read();
                    var first = Choose(true);
                    var second = Choose(false);
                    var either = Choose(flag);
                }
            }
            """);
        var analysis = new ErrorFlowAnalysis(compilation);
        var expressions = new[] { "reader.Read()", "Choose(true)", "Choose(false)", "Choose(flag)" }
            .Select(text => Expression(compilation, text)).ToArray();
        var results = analysis.AnalyzeExpressions(ApiMethod(compilation, "Api", "Run"), expressions);

        Assert.True(results[0].Summary!.HasUnknownErrors);
        AssertExpressionErrors(results[1], "First");
        AssertExpressionErrors(results[2], "Second");
        AssertExpressionErrors(results[3], "First", "Second");
        Assert.All(results, result => Assert.Equal(ErrorFlowSummaryOrigin.SourceBody, result.Summary!.Origin));
    }

    [Theory]
    [InlineData("var result = flag ? Rail.Fail<int>(new First()) : Rail.Fail<int>(new Second());", "flag ? Rail.Fail<int>(new First()) : Rail.Fail<int>(new Second())")]
    [InlineData("Error? error = null; var result = error ?? new First();", "error ?? new First()")]
    [InlineData("RailFor<int> result = new First(); var copy = ((result));", "((result))")]
    public void ExpressionApi_ObservesLoweredAndParenthesizedValues(string statements, string expression)
    {
        var compilation = ExpressionCompilation($$"""
            using FlowOnRails;
            public sealed record First : Error;
            public sealed record Second : Error;
            public static class Api { public static void Run(bool flag) { {{statements}} } }
            """);
        var result = Assert.Single(new ErrorFlowAnalysis(compilation).AnalyzeExpressions(
            ApiMethod(compilation, "Api", "Run"), [Expression(compilation, expression)]));

        Assert.Equal(ErrorFlowExpressionStatus.Analyzed, result.Status);
        Assert.NotNull(result.Summary);
        Assert.Contains(result.Summary.KnownErrorTypes, type => type.Name == "First");
        Assert.False(result.Summary.HasUnknownErrors);
    }

    [Fact]
    public void ExpressionApi_DistinguishesUnreachableUnsupportedAndNoNormalResult()
    {
        var compilation = ExpressionCompilation("""
            using System;
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Api
            {
                static RailFor<int> Stop() => throw new Exception();
                public static void Run()
                {
                    if (false) { var dead = Rail.Fail<int>(new Failure()); }
                    var text = "unsupported";
                    var stopped = Stop();
                }
            }
            """);
        var expressions = new[] { "Rail.Fail<int>(new Failure())", "\"unsupported\"", "Stop()" }
            .Select(text => Expression(compilation, text));
        var results = new ErrorFlowAnalysis(compilation).AnalyzeExpressions(ApiMethod(compilation, "Api", "Run"), expressions);

        Assert.Equal(ErrorFlowExpressionStatus.Unreachable, results[0].Status);
        Assert.Null(results[0].Summary);
        Assert.Empty(results[0].LimitationDetails);
        Assert.Equal(ErrorFlowExpressionStatus.Unsupported, results[1].Status);
        Assert.Null(results[1].Summary);
        Assert.Contains(results[1].LimitationDetails, limit => limit.Code == ErrorFlowLimitationCode.UnsupportedOperation);
        Assert.Equal(ErrorFlowExpressionStatus.Analyzed, results[2].Status);
        Assert.False(results[2].Summary!.MayProduceResult);
    }

    [Fact]
    public void ExpressionApi_JoinsLoopVisitsAndPreservesErrorFamilyExclusions()
    {
        var compilation = ExpressionCompilation("""
            using FlowOnRails;
            public record Parent : Error;
            public sealed record Child : Parent;
            public sealed record First : Error;
            public sealed record Second : Error;
            public static class Api
            {
                static void Observe(RailFor<int> value) { }
                public static void Run(bool flag, Parent error)
                {
                    RailFor<int> result = new First();
                    do { Observe(result); result = new Second(); } while (flag);
                    if (error is not Child) { var filtered = Rail.Fail<int>(error); }
                }
            }
            """);
        var results = new ErrorFlowAnalysis(compilation).AnalyzeExpressions(ApiMethod(compilation, "Api", "Run"),
            [ObservedArguments(compilation).Single(), Expression(compilation, "Rail.Fail<int>(error)")]);

        AssertExpressionErrors(results[0], "First", "Second");
        var family = Assert.Single(results[1].Summary!.ErrorCases);
        Assert.Equal("Parent", family.Type.Name);
        Assert.False(family.IsExact);
        Assert.Equal("Child", Assert.Single(family.ExcludedTypes).Name);
    }

    [Fact]
    public void ExpressionApi_TracksAwaitableValuesAndExpressionsAfterAwait()
    {
        var compilation = ExpressionCompilation("""
            using System.Threading.Tasks;
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Api
            {
                public static async Task<string> Run()
                {
                    var task = Task.FromResult(Rail.Fail<int>(new Failure()));
                    var result = await task;
                    return "done";
                }
            }
            """);
        var results = new ErrorFlowAnalysis(compilation).AnalyzeExpressions(ApiMethod(compilation, "Api", "Run"),
            [Expression(compilation, "Task.FromResult(Rail.Fail<int>(new Failure()))"), Expression(compilation, "await task")]);

        Assert.All(results, result => AssertExpressionErrors(result, "Failure"));
    }

    [Theory]
    [InlineData("Func<RailFor<int>> handler = () => new Failure();", "new Failure()")]
    [InlineData("RailFor<int> Handler() => new Failure();", "new Failure()")]
    [InlineData("Func<Func<RailFor<int>>> outer = () => () => new Failure();", "new Failure()")]
    public void NestedFunctionApi_QueriesBodiesWithoutCallingTheirOuterMethod(string declaration, string text)
    {
        var compilation = ExpressionCompilation($$"""
            using System;
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Api { public static void Register() { {{declaration}} } }
            """);
        var expression = Expression(compilation, text);
        var method = ExpressionOwner(compilation, expression);
        var analysis = new ErrorFlowAnalysis(compilation);

        Assert.Equal("Failure", Assert.Single(analysis.AnalyzeMethod(method).KnownErrorTypes).Name);
        AssertExpressionErrors(Assert.Single(analysis.AnalyzeExpressions(method, [expression])), "Failure");
        Assert.Throws<ArgumentException>(() => analysis.AnalyzeExpressions(ApiMethod(compilation, "Api", "Register"), [expression]));
    }

    [Theory]
    [InlineData("static readonly Func<RailFor<int>> Handler = () => new Failure();")]
    [InlineData("static Func<RailFor<int>> Handler { get; } = () => new Failure();")]
    public void NestedFunctionApi_ResolvesLambdasInInitializers(string declaration)
    {
        var compilation = ExpressionCompilation($$"""
            using System;
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Api { {{declaration}} }
            """);
        var expression = Expression(compilation, "new Failure()");
        var method = ExpressionOwner(compilation, expression);
        var analysis = new ErrorFlowAnalysis(compilation);

        AssertExpressionErrors(Assert.Single(analysis.AnalyzeExpressions(method, [expression])), "Failure");
        Assert.Equal("Failure", Assert.Single(analysis.AnalyzeMethod(method).KnownErrorTypes).Name);
    }

    [Fact]
    public void NestedFunctionApi_KeepsStandaloneCapturesUnknownAndKnownCallsSpecialized()
    {
        var compilation = ExpressionCompilation("""
            using System;
            using FlowOnRails;
            public sealed record First : Error;
            public sealed record Second : Error;
            public static class Api
            {
                public static RailFor<int> Run()
                {
                    bool first = true;
                    Func<RailFor<int>> handler = () => first ? new First() : new Second();
                    first = false;
                    return handler();
                }
            }
            """);
        var expression = Expression(compilation, "first ? new First() : new Second()");
        var method = ExpressionOwner(compilation, expression);
        var analysis = new ErrorFlowAnalysis(compilation);
        var standalone = analysis.AnalyzeMethod(method);

        Assert.Equal(new[] { "First", "Second" }, standalone.KnownErrorTypes.Select(type => type.Name).Order());
        Assert.Contains(standalone.LimitationDetails, limit => limit.Code == ErrorFlowLimitationCode.CapturedInputUnknown);
        AssertExpressionErrors(Assert.Single(analysis.AnalyzeExpressions(method, [expression])), "First", "Second");
        Assert.Equal("Second", Assert.Single(analysis.AnalyzeMethod(ApiMethod(compilation, "Api", "Run")).KnownErrorTypes).Name);
    }

    [Fact]
    public void ExpressionApi_UsesMetadataForCallsInTheQueriedBody()
    {
        var reference = EmitProducer(ProducerSource, true, out _);
        var compilation = ExpressionCompilation("public static class Consumer { public static void Run() { var value = Producer.Api.Direct(); } }", reference);
        var result = Assert.Single(new ErrorFlowAnalysis(compilation).AnalyzeExpressions(
            ApiMethod(compilation, "Consumer", "Run"), [Expression(compilation, "Producer.Api.Direct()") ]));

        AssertExpressionErrors(result, "FirstFailure");
        Assert.Equal(ErrorFlowSummaryOrigin.SourceBody, result.Summary!.Origin);
    }

    [Fact]
    public void ExpressionApi_PreservesOrderAndDuplicatesAndRejectsInvalidScopes()
    {
        const string source = "using FlowOnRails; public class Api { public void Run() { var first = Rail.Ok(1); var second = Rail.Ok(2); } public void Other() { var other = Rail.Ok(3); } }";
        var compilation = ExpressionCompilation(source);
        var foreign = ExpressionCompilation(source);
        var analysis = new ErrorFlowAnalysis(compilation);
        var method = ApiMethod(compilation, "Api", "Run");
        var first = Expression(compilation, "Rail.Ok(1)");
        var second = Expression(compilation, "Rail.Ok(2)");
        var results = analysis.AnalyzeExpressions(method, [second, first, second]);

        Assert.Equal(new[] { second, first, second }, results.Select(result => result.Expression));
        Assert.All(results, result => Assert.True(result.Summary!.MaySucceed));
        Assert.Empty(analysis.AnalyzeExpressions(method, []));
        Assert.Throws<ArgumentNullException>(() => analysis.AnalyzeExpressions(null!, []));
        Assert.Throws<ArgumentNullException>(() => analysis.AnalyzeExpressions(method, null!));
        Assert.Throws<ArgumentException>(() => analysis.AnalyzeExpressions(ApiMethod(foreign, "Api", "Run"), []));
        Assert.Throws<ArgumentException>(() => analysis.AnalyzeExpressions(method, [Expression(foreign, "Rail.Ok(1)")]));
        Assert.Throws<ArgumentException>(() => analysis.AnalyzeExpressions(method, [Expression(compilation, "Rail.Ok(3)")]));
        Assert.Throws<ArgumentException>(() => analysis.AnalyzeExpressions(method, [null!]));
        Assert.Throws<OperationCanceledException>(() => analysis.AnalyzeExpressions(method, [first], new CancellationToken(true)));
    }

    [Fact]
    public async Task ExpressionApi_SupportsConcurrentIndependentQueries()
    {
        var compilation = ExpressionCompilation("using FlowOnRails; public static class Api { public static void Run() { var value = Rail.Ok(1); } }");
        var analysis = new ErrorFlowAnalysis(compilation);
        var method = ApiMethod(compilation, "Api", "Run");
        var expression = Expression(compilation, "Rail.Ok(1)");
        var queries = Enumerable.Range(0, 12).Select(_ => Task.Run(() => analysis.AnalyzeExpressions(method, [expression])));

        foreach (var results in await Task.WhenAll(queries))
            Assert.True(Assert.Single(results).Summary!.MaySucceed);
    }

    [Fact]
    public void NestedFunctionApi_ResolvesTopLevelHandlersAndTheirContainingBody()
    {
        var compilation = ApiCompilation("""
            using System;
            using FlowOnRails;
            Func<RailFor<int>> handler = () => new Failure();
            var value = Rail.Ok(1);
            public sealed record Failure : Error;
            """).WithOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var expression = Expression(compilation, "new Failure()");
        var analysis = new ErrorFlowAnalysis(compilation);

        AssertExpressionErrors(Assert.Single(analysis.AnalyzeExpressions(ExpressionOwner(compilation, expression), [expression])), "Failure");
        var point = Expression(compilation, "Rail.Ok(1)");
        var result = Assert.Single(analysis.AnalyzeExpressions(compilation.GetEntryPoint(default)!, [point]));
        Assert.True(result.Summary!.MaySucceed);
    }

    [Fact]
    public void ExpressionApi_QueriesConstructorBodiesAndVirtualImplementations()
    {
        var compilation = ExpressionCompilation("""
            using FlowOnRails;
            public sealed record Failure : Error;
            public class Api
            {
                public Api() { var value = Rail.Fail<int>(new Failure()); }
                public virtual RailFor<int> Read() => Rail.Fail<int>(new Failure());
            }
            """);
        var analysis = new ErrorFlowAnalysis(compilation);
        var expressions = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression.ToString() == "Rail.Fail<int>").ToArray();

        foreach (var expression in expressions)
            AssertExpressionErrors(Assert.Single(analysis.AnalyzeExpressions(ExpressionOwner(compilation, expression), [expression])), "Failure");

        Assert.True(analysis.AnalyzeMethod(ApiMethod(compilation, "Api", "Read")).HasUnknownErrors);
    }

    [Fact]
    public void ExpressionApi_DoesNotTreatWriteDestinationsAsUnreachableReads()
    {
        var compilation = ExpressionCompilation("""
            using FlowOnRails;
            public static class Api
            {
                static void Fill(out RailFor<int> value) => value = Rail.Ok(1);
                public static void Run()
                {
                    RailFor<int> first = default, second = default;
                    first = Rail.Ok(1);
                    (first, second) = (Rail.Ok(2), Rail.Ok(3));
                    Fill(out second);
                }
            }
            """);
        var method = ApiMethod(compilation, "Api", "Run");
        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.Single());
        var destinations = compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(expression => expression.Identifier.ValueText is "first" or "second"
                && SymbolEqualityComparer.Default.Equals(model.GetEnclosingSymbol(expression.SpanStart), method));
        var results = new ErrorFlowAnalysis(compilation).AnalyzeExpressions(method, destinations);

        Assert.Equal(4, results.Length);
        Assert.All(results, result => Assert.Equal(ErrorFlowExpressionStatus.Unsupported, result.Status));
    }

    [Fact]
    public void ExpressionApi_KeepsResultsOpenWhenTheBodyExceedsItsAnalysisBudget()
    {
        var methods = string.Join("\n", Enumerable.Range(0, 40).Select(index =>
            $"static RailFor<int> Read{index}() => Read{index + 1}();"));
        var compilation = ExpressionCompilation($$"""
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Api
            {
                {{methods}}
                static RailFor<int> Read40() => new Failure();
                public static void Run()
                {
                    var first = Rail.Fail<int>(new Failure());
                    var deep = Read0();
                    if (false) { var dead = Rail.Ok(1); }
                }
            }
            """);
        var results = new ErrorFlowAnalysis(compilation).AnalyzeExpressions(ApiMethod(compilation, "Api", "Run"),
            [Expression(compilation, "Rail.Fail<int>(new Failure())"), Expression(compilation, "Read0()"), Expression(compilation, "Rail.Ok(1)")]);

        Assert.Equal(ErrorFlowExpressionStatus.Analyzed, results[0].Status);
        Assert.Contains(results[0].Summary!.KnownErrorTypes, type => type.Name == "Failure");
        Assert.True(results[0].Summary!.HasUnknownErrors);
        Assert.True(results[1].Summary!.HasUnknownErrors);
        Assert.Contains(results[0].LimitationDetails, limit => limit.Code == ErrorFlowLimitationCode.AnalysisBudgetExceeded);
        Assert.Equal(ErrorFlowExpressionStatus.Unsupported, results[2].Status);
        Assert.Null(results[2].Summary);
    }

    [Fact]
    public void ExpressionApi_SubstitutesConstructedGenericBodyTypes()
    {
        var compilation = ExpressionCompilation("""
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Api
            {
                public static void Run<T>() where T : Error, new() { var value = Rail.Fail<int>(new T()); }
            }
            """);
        var method = ApiMethod(compilation, "Api", "Run").Construct(compilation.GetTypeByMetadataName("Failure")!);
        var result = Assert.Single(new ErrorFlowAnalysis(compilation).AnalyzeExpressions(method,
            [Expression(compilation, "Rail.Fail<int>(new T())")]));

        AssertExpressionErrors(result, "Failure");
    }

    [Fact]
    public void ExpressionApi_AcceptsPartialMethodDefinitionsAndImplementations()
    {
        var compilation = ExpressionCompilation("""
            using FlowOnRails;
            public sealed record Failure : Error;
            public partial class Api
            {
                partial void Run();
                partial void Run() { var value = Rail.Fail<int>(new Failure()); }
            }
            """);
        var definition = ApiMethod(compilation, "Api", "Run");
        var implementation = definition.PartialImplementationPart;
        Assert.NotNull(implementation);
        var expression = Expression(compilation, "Rail.Fail<int>(new Failure())");
        var analysis = new ErrorFlowAnalysis(compilation);

        AssertExpressionErrors(Assert.Single(analysis.AnalyzeExpressions(definition, [expression])), "Failure");
        AssertExpressionErrors(Assert.Single(analysis.AnalyzeExpressions(implementation, [expression])), "Failure");
    }

    [Fact]
    public void ExpressionApi_DoesNotEvaluateArgumentsAfterANonReturningCall()
    {
        var compilation = ExpressionCompilation("""
            using System;
            using FlowOnRails;
            public sealed record Failure : Error;
            public sealed record CombinedFailure : Error;
            public static class Api
            {
                static RailFor<int> Stop() => throw new Exception();
                static RailFor<int> Combine(RailFor<int> first, RailFor<int> second) => new CombinedFailure();
                public static void Run() { var value = Combine(Stop(), Rail.Fail<int>(new Failure())); }
            }
            """);
        var results = new ErrorFlowAnalysis(compilation).AnalyzeExpressions(ApiMethod(compilation, "Api", "Run"),
            [Expression(compilation, "Stop()"), Expression(compilation, "Rail.Fail<int>(new Failure())"),
                Expression(compilation, "Combine(Stop(), Rail.Fail<int>(new Failure()))")]);

        Assert.Equal(ErrorFlowExpressionStatus.Analyzed, results[0].Status);
        Assert.False(results[0].Summary!.MayProduceResult);
        Assert.Equal(ErrorFlowExpressionStatus.Unreachable, results[1].Status);
        Assert.Null(results[1].Summary);
        Assert.Equal(ErrorFlowExpressionStatus.Analyzed, results[2].Status);
        Assert.False(results[2].Summary!.MayProduceResult);
        Assert.Empty(results[2].Summary!.ErrorCases);
    }

    [Fact]
    public void ExpressionApi_RecognizesErrorsFromConstructedUnconstrainedTypeParameters()
    {
        var compilation = ExpressionCompilation("""
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Api
            {
                public static void Run<T>() where T : new() { var value = new T(); }
            }
            """);
        var method = ApiMethod(compilation, "Api", "Run").Construct(compilation.GetTypeByMetadataName("Failure")!);
        var result = Assert.Single(new ErrorFlowAnalysis(compilation).AnalyzeExpressions(method,
            [Expression(compilation, "new T()")]));

        AssertExpressionErrors(result, "Failure");
    }

    [Fact]
    public void ExpressionApi_DoesNotApplyCallbackEffectsOfAnUnevaluatedConstructor()
    {
        var compilation = ExpressionCompilation("""
            using System;
            using FlowOnRails;
            public sealed record Failure : Error;
            public sealed class Container
            {
                public Container(RailFor<int> first, RailFor<int> second, Func<int> callback) { }
            }
            public static class Api
            {
                static RailFor<int> Stop() => throw new Exception();
                public static void Run()
                {
                    var value = new Container(Stop(), Rail.Fail<int>(new Failure()), () => 42);
                }
            }
            """);
        var result = Assert.Single(new ErrorFlowAnalysis(compilation).AnalyzeExpressions(ApiMethod(compilation, "Api", "Run"),
            [Expression(compilation, "Rail.Fail<int>(new Failure())")]));

        Assert.Equal(ErrorFlowExpressionStatus.Unreachable, result.Status);
        Assert.Empty(result.LimitationDetails);
    }

    private static CSharpCompilation ExpressionCompilation(string source, MetadataReference? reference = null)
    {
        var compilation = ApiCompilation(source, reference);
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        return compilation;
    }

    private static ExpressionSyntax Expression(Compilation compilation, string text) =>
        compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<ExpressionSyntax>()
            .Single(expression => expression.ToString() == text);

    private static ExpressionSyntax[] ObservedArguments(Compilation compilation) =>
        compilation.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => invocation.Expression.ToString() == "Observe")
            .Select(invocation => invocation.ArgumentList.Arguments.Single().Expression).ToArray();

    private static IMethodSymbol ExpressionOwner(Compilation compilation, ExpressionSyntax expression) =>
        (IMethodSymbol)compilation.GetSemanticModel(expression.SyntaxTree).GetEnclosingSymbol(expression.SpanStart)!;

    private static void AssertExpressionErrors(ErrorFlowExpressionResult result, params string[] names)
    {
        Assert.Equal(ErrorFlowExpressionStatus.Analyzed, result.Status);
        Assert.NotNull(result.Summary);
        Assert.Equal(names.Order(), result.Summary.KnownErrorTypes.Select(type => type.Name).Order());
        Assert.False(result.Summary.HasUnknownErrors);
    }
}
