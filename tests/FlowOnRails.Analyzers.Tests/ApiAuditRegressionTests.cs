using FlowOnRails.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    [Theory]
    [InlineData("Change(ref rail).Value = 1;", "After")]
    [InlineData("Change(ref rail).Field = 1;", "After")]
    [InlineData("Change(ref rail)[0] = 1;", "After")]
    [InlineData("Array(ref rail)[0] = 1;", "After")]
    [InlineData("new Box()[Index(ref rail)] = 1;", "After")]
    [InlineData("Change(ref rail).Value += 1;", "After")]
    [InlineData("Change(ref rail).Value++;", "After")]
    [InlineData("(Change(ref rail).Value, _) = (1, 2);", "After")]
    [InlineData("Change(ref rail).Value = Right(ref rail);", "Final")]
    [InlineData("Change(ref rail).Value += Right(ref rail);", "Final")]
    public void WriteDestinations_PreserveEffectsAndEvaluationOrderLocallyAndInMetadata(string assignment, string expectedError)
    {
        var source = $$"""
            using FlowOnRails;
            public sealed record Before : Error;
            public sealed record After : Error;
            public sealed record Final : Error;
            public sealed class Box
            {
                public int Value { get; set; }
                public int Field;
                public int this[int index] { get => 0; set { } }
            }
            public static class Api
            {
                private static Box Change(ref RailFor<int> rail) { rail = new After(); return new Box(); }
                private static int[] Array(ref RailFor<int> rail) { rail = new After(); return new int[1]; }
                private static int Index(ref RailFor<int> rail) { rail = new After(); return 0; }
                private static int Right(ref RailFor<int> rail) { rail = new Final(); return 1; }
                public static RailFor<int> Read()
                {
                    RailFor<int> rail = new Before();
                    {{assignment}}
                    return rail;
                }
            }
            """;
        var local = ApiCompilation(source);
        var exported = ApiCompilation("class Consumer { }", EmitProducer(source, true, out _));

        foreach (var compilation in new[] { local, exported })
        {
            var summary = new ErrorFlowAnalysis(compilation).AnalyzeMethod(ApiMethod(compilation, "Api", "Read"));

            Assert.Equal(expectedError, Assert.Single(summary.KnownErrorTypes).Name);
            Assert.False(summary.HasUnknownErrors);
        }
    }

    [Fact]
    public void SimplePropertyWrite_DoesNotRunItsGetter()
    {
        var compilation = ApiCompilation("""
            using FlowOnRails;
            public sealed record Failure : Error;
            public sealed class Box { public int Value { get => throw new System.Exception(); set { } } }
            public static class Api
            {
                public static RailFor<int> Read() { new Box().Value = 1; return new Failure(); }
            }
            """);
        var summary = new ErrorFlowAnalysis(compilation).AnalyzeMethod(ApiMethod(compilation, "Api", "Read"));

        Assert.True(summary.MayProduceResult);
        Assert.Equal("Failure", Assert.Single(summary.KnownErrorTypes).Name);
    }

    [Fact]
    public void GenericTypeTests_PreserveUnboundAlternativesAndSpecializeLocalCalls()
    {
        const string source = """
            using FlowOnRails;
            public sealed record Before : Error;
            public sealed record After : Error;
            public static class Api
            {
                public static RailFor<int> Read<TError>() where TError : Error
                {
                    Error error = new Before();
                    return error is TError ? new After() : new Before();
                }
                public static RailFor<int> Local() => Read<Before>();
            }
            """;
        var local = ApiCompilation(source);
        var analysis = new ErrorFlowAnalysis(local);
        var generic = analysis.AnalyzeMethod(ApiMethod(local, "Api", "Read"));
        var specialized = analysis.AnalyzeMethod(ApiMethod(local, "Api", "Local"));
        var consumer = ApiCompilation("class Consumer { }", EmitProducer(source, true, out _));
        var external = new ErrorFlowAnalysis(consumer).AnalyzeMethod(ApiMethod(consumer, "Api", "Read")
            .Construct(consumer.GetTypeByMetadataName("Before")!));

        Assert.Equal(new[] { "After", "Before" }, generic.KnownErrorTypes.Select(type => type.Name).Order());
        Assert.True(generic.HasUnresolvedConditions);
        Assert.Equal("After", Assert.Single(specialized.KnownErrorTypes).Name);
        Assert.Contains(external.KnownErrorTypes, type => type.Name == "After");
        Assert.True(external.HasUnresolvedConditions);
    }

    [Theory]
    [InlineData("null!")]
    [InlineData("default!")]
    [InlineData("default(System.Threading.Tasks.Task<FlowOnRails.RailFor<int>>)! ")]
    public async Task NullTask_HasNoAwaitedResultAndDoesNotCrashMetadataConsumers(string expression)
    {
        var source = $$"""
            using System.Threading.Tasks;
            using FlowOnRails;
            public static class Api { public static Task<RailFor<int>> Read() => {{expression}}; }
            """;
        var reference = EmitProducer(source, true, out _);
        var local = ApiCompilation(source);
        var consumer = ApiCompilation("class Consumer { }", reference);

        foreach (var compilation in new[] { local, consumer })
        {
            var summary = new ErrorFlowAnalysis(compilation).AnalyzeMethod(ApiMethod(compilation, "Api", "Read"));

            Assert.False(summary.MayProduceResult);
            Assert.False(summary.MayReturnNull);
            Assert.False(summary.HasUnknownErrors);
            Assert.Empty(summary.ErrorCases);
        }

        await AnalyzeSource("""
            using System.Threading.Tasks;
            using FlowOnRails;
            public static class Consumer { public static Task<RailFor<int>> Read() => Api.Read(); }
            """, true, false, "Read", [reference]);
    }

    [Fact]
    public async Task WrongShapeMetadata_FallsBackToOpenInsteadOfThrowing()
    {
        // Schema 1: a valid Error/null value encoded for a Rail-returning method.
        // This exercises InvalidDataException after decoding rather than base64 errors.
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)2);
            writer.Write(false);
            writer.Write(false);
            writer.Write(true);
            writer.Write(-1);
            writer.Write(-1);
            writer.Write(0);
            writer.Write(false);
            writer.Write(0);
        }

        var payload = Convert.ToBase64String(stream.ToArray());
        var source = $$"""
            using FlowOnRails;
            [assembly: FlowAnalysisSummaryAttribute(1, "M:Producer.Api.Direct~FlowOnRails.RailFor{System.Int32}", "{{payload}}")]
            namespace Producer;
            public static class Api { public static RailFor<int> Direct() => Rail.Ok(1); }
            """;
        var reference = EmitProducer(source, false, out _);
        var consumer = ApiCompilation("class Consumer { }", reference);
        var summary = new ErrorFlowAnalysis(consumer).AnalyzeMethod(ApiMethod(consumer, "Producer.Api", "Direct"));

        Assert.True(summary.HasUnknownErrors);
        Assert.Contains(summary.Limitations, limitation => limitation.Contains("invalid"));
        var diagnostic = Assert.Single(await AnalyzeConsumer("return Api.Direct();", "RailFor<int>", false, reference));
        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public void NullErrorInsideATask_RemainsANormalNullableErrorResult()
    {
        const string source = """
            using FlowOnRails;
            using System.Threading.Tasks;
            public static class Api { public static Task<Error?> Read() => Task.FromResult<Error?>(null); }
            """;
        var local = ApiCompilation(source);
        var consumer = ApiCompilation("class Consumer { }", EmitProducer(source, true, out _));

        foreach (var compilation in new[] { local, consumer })
        {
            var summary = new ErrorFlowAnalysis(compilation).AnalyzeMethod(ApiMethod(compilation, "Api", "Read"));

            Assert.True(summary.MayProduceResult);
            Assert.True(summary.MayReturnNull);
            Assert.Empty(summary.ErrorCases);
        }
    }

    [Fact]
    public void PublicApi_CanAnalyzeRejectsUnsupportedAndForeignMethodsWithoutExecutingThem()
    {
        const string source = """
            using FlowOnRails;
            using System.Threading.Tasks;
            public interface IReader { RailFor<int> Read(); }
            public static class Api
            {
                public static Task<RailFor<int>> Read() => null!;
                public static int Count() => 1;
            }
            """;
        var compilation = ApiCompilation(source);
        var foreign = ApiCompilation(source);
        var analysis = new ErrorFlowAnalysis(compilation);

        Assert.False(analysis.CanAnalyze(null));
        Assert.False(analysis.CanAnalyze(ApiMethod(compilation, "Api", "Count")));
        Assert.False(analysis.CanAnalyze(ApiMethod(foreign, "Api", "Read")));
        Assert.True(analysis.CanAnalyze(ApiMethod(compilation, "Api", "Read")));
        Assert.True(analysis.CanAnalyze(ApiMethod(compilation, "IReader", "Read")));
    }

    [Fact]
    public async Task Messages_DistinguishCallsAndMethodsWithoutDuplicateEmptySummaries()
    {
        var diagnostics = await AnalyzeSource("""
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Api
            {
                public static RailFor<int> Read() => new Failure();
                public static void Run() { _ = Read(); }
            }
            """, true, false, null);
        var declaration = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW001");
        var invocation = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW002");

        Assert.StartsWith("This method may return errors:", declaration.GetMessage());
        Assert.StartsWith("This call may return errors:", invocation.GetMessage());
        var noReturn = Assert.Single(await Analyze("throw new Exception();"));
        Assert.Equal("No normal result was identified for this method in the supported flow.", noReturn.GetMessage());
        var invalid = Assert.Single(await Analyze("return default;"));
        Assert.Contains("An uninitialized rail may be returned.", invalid.GetMessage());
        Assert.DoesNotContain("also", invalid.GetMessage());
    }

    [Theory]
    [InlineData("return error $$switch { IRetryable => 1, _ => 0 };")]
    [InlineData("$$switch (error) { case IRetryable retryable: return 1; default: return 0; }")]
    [InlineData("return error $$switch { IRetryable<object> => 1, _ => 0 };")]
    public async Task SwitchAction_InterfaceCoverageDoesNotCreateUnreachableCases(string switchCode)
    {
        using var workspace = new AdhocWorkspace();
        var source = $$"""
            using FlowOnRails;
            public interface IRetryable { }
            public interface IRetryable<out TValue> { }
            public sealed record Missing : Error, IRetryable, IRetryable<string>;
            public sealed record Other : Error;
            public static class Api
            {
                public static int Read(bool flag)
                {
                    Error error = flag ? new Missing() : new Other();
                    {{switchCode}}
                }
            }
            """;
        var position = source.IndexOf("$$", StringComparison.Ordinal);
        var document = AddEditorProject(workspace.CurrentSolution, "Study")
            .AddDocument("Study.cs", SourceText.From(source.Replace("$$", "")));
        var solution = await ApplyEditorAction(Assert.Single(await SwitchActions(document, position)));
        var text = (await solution.GetDocument(document.Id)!.GetTextAsync()).ToString();

        Assert.DoesNotContain("Missing =>", text);
        Assert.DoesNotContain("case Missing", text);
        Assert.True(text.Contains("Other =>") || text.Contains("case Other"), text);
        await AssertEditorSolutionCompiles(solution);
        Assert.Empty(await SwitchActions(solution.GetDocument(document.Id)!, text.IndexOf("switch", StringComparison.Ordinal)));
    }
}
