using FlowOnRails.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    [Fact]
    public void PublicApi_ReportsSymbolsAndSeparatesOpenContractsFromConcreteBodies()
    {
        var compilation = ApiCompilation("""
            using FlowOnRails;
            public sealed record Missing : Error;
            public interface IReader { [ResultsWith<Missing>] RailFor<int> Read(); }
            public sealed class Reader : IReader { public RailFor<int> Read() => new Missing(); }
            """);
        var analysis = new ErrorFlowAnalysis(compilation);
        var contract = analysis.AnalyzeMethod(ApiMethod(compilation, "IReader", "Read"));
        var implementation = analysis.AnalyzeMethod(ApiMethod(compilation, "Reader", "Read"));

        Assert.True(contract.HasUnknownErrors);
        Assert.Equal("Missing", Assert.Single(contract.KnownErrorTypes).Name);
        Assert.Contains(contract.ErrorCases, errorCase => errorCase.Type.Name == "Error" && !errorCase.IsExact);
        Assert.False(implementation.HasUnknownErrors);
        Assert.True(Assert.Single(implementation.ErrorCases).IsExact);
        Assert.False(implementation.MaySucceed);
        Assert.True(implementation.MayProduceResult);
        Assert.Empty(implementation.Limitations);
    }

    [Fact]
    public void PublicApi_ReadsMetadataAndKeepsMissingMetadataOpen()
    {
        var reference = EmitProducer(ProducerSource, true, out _);
        var compilation = ApiCompilation("class Consumer { }", reference);
        var analysis = new ErrorFlowAnalysis(compilation);
        var summary = analysis.AnalyzeMethod(ApiMethod(compilation, "Producer.Api", "Direct"));

        Assert.False(summary.HasUnknownErrors);
        Assert.Equal("Producer.FirstFailure", Assert.Single(summary.KnownErrorTypes).ToDisplayString());

        var missingReference = EmitProducer(ProducerSource, false, out _);
        var missingCompilation = ApiCompilation("class Consumer { }", missingReference);
        var unknown = new ErrorFlowAnalysis(missingCompilation)
            .AnalyzeMethod(ApiMethod(missingCompilation, "Producer.Api", "Direct"));

        Assert.True(unknown.HasUnknownErrors);
        Assert.Contains(unknown.Limitations, limitation => limitation.Contains("unavailable"));
    }

    [Fact]
    public void PublicApi_PreservesFamilyExclusionsAndAsyncResultStates()
    {
        var compilation = ApiCompilation("""
            using FlowOnRails;
            using System.Threading.Tasks;
            public record Parent : Error;
            public sealed record Child : Parent;
            public static class Api
            {
                public static RailFor<int> Filter(Parent error) => error is Child ? Rail.Ok(42) : Rail.Fail<int>(error);
                public static async Task<RailFor<int>> Success() { await Task.Yield(); return Rail.Ok(42); }
                public static RailFor<int> Invalid() => default;
            }
            """);
        var analysis = new ErrorFlowAnalysis(compilation);
        var filtered = analysis.AnalyzeMethod(ApiMethod(compilation, "Api", "Filter"));
        var family = Assert.Single(filtered.ErrorCases);

        Assert.True(filtered.HasUnknownErrors);
        Assert.Equal("Parent", family.Type.Name);
        Assert.Equal("Child", Assert.Single(family.ExcludedTypes).Name);
        Assert.True(analysis.AnalyzeMethod(ApiMethod(compilation, "Api", "Success")).MaySucceed);
        Assert.True(analysis.AnalyzeMethod(ApiMethod(compilation, "Api", "Invalid")).MayBeInvalid);
    }

    [Fact]
    public void PublicApi_RejectsForeignSymbolsUnsupportedReturnsAndCancellation()
    {
        const string source = "using FlowOnRails; public static class Api { public static RailFor<int> Read() => Rail.Ok(1); public static int Count() => 1; }";
        var compilation = ApiCompilation(source);
        var analysis = new ErrorFlowAnalysis(compilation);
        var otherCompilation = ApiCompilation(source);

        Assert.Throws<ArgumentException>(() => analysis.AnalyzeMethod(ApiMethod(otherCompilation, "Api", "Read")));
        Assert.Throws<ArgumentException>(() => analysis.AnalyzeMethod(ApiMethod(compilation, "Api", "Count")));
        Assert.Throws<OperationCanceledException>(() => analysis.AnalyzeMethod(ApiMethod(compilation, "Api", "Read"), new CancellationToken(true)));
    }

    private static CSharpCompilation ApiCompilation(string source, MetadataReference? reference = null) =>
        CSharpCompilation.Create("ApiConsumer", [CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.CSharp14))],
            reference is null ? References : References.Add(reference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static IMethodSymbol ApiMethod(Compilation compilation, string typeName, string methodName) =>
        compilation.GetTypeByMetadataName(typeName)!.GetMembers(methodName).OfType<IMethodSymbol>().Single();
}
