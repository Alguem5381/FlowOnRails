using System.Collections.Immutable;

using FlowOnRails.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    private const string ProducerSource = """
        using System;
        using System.Threading.Tasks;
        using FlowOnRails;
        namespace Producer;
        public sealed record FirstFailure : Error;
        public sealed record SecondFailure : Error;
        public sealed record GenericFailure<TValue> : Error;
        public interface IReader
        {
            [ResultsWith<FirstFailure>]
            Task<RailFor<int>> Read();
        }
        public static partial class Api
        {
            public static RailFor<int> Direct() => new FirstFailure();
            public static RailFor<TValue> Forward<TValue>(RailFor<TValue> result) where TValue : notnull => result;
            public static Error Keep(Error error) => error;
            public static partial RailFor<int> Partial(RailFor<int> result);
            public static partial RailFor<int> Partial(RailFor<int> result) => result;
            public static partial RailFor<int> PartialGeneric<TValue>();
            public static partial RailFor<int> PartialGeneric<TValue>() => new GenericFailure<TValue>();
            public static RailFor<int> Create<TError>() where TError : Error, new() => new TError();
            public static RailFor<int> Generic<TValue>() => new GenericFailure<TValue>();
            public static Task<RailFor<int>> AsyncFactory() => Task.FromResult(Rail.Fail<int>(new SecondFailure()));
            public static async ValueTask<RailFor<int>> AsyncBody()
            {
                await Task.Yield();
                return new SecondFailure();
            }
            public static Task<RailFor<TValue>> Wrap<TValue>(RailFor<TValue> result) where TValue : notnull => Task.FromResult(result);
            public static RailFor<int> Hidden(bool flag) => flag ? new HiddenFailure() : new SecondFailure();
            public static Task<RailFor<int>> Open(IReader reader) => reader.Read();
            public static RailFor<int> NestedRailValue()
            {
                RailFor<Rail> wrapped = Rail.Fail(new FirstFailure());

                return wrapped.IsSuccess ? new SecondFailure() : new FirstFailure();
            }
            public static RailFor<int> ConstructorCallback()
            {
                RailFor<int> result = new FirstFailure();
                void Change() => result = new SecondFailure();
                _ = new Invoke(Change);

                return result;
            }
            private sealed class Invoke
            {
                public Invoke(Action action) => action();
            }
            private sealed record HiddenFailure : Error;
        }
        public static class Container<TValue>
        {
            public static RailFor<int> Create() => new GenericFailure<TValue>();
        }
        """;

    [Theory]
    [InlineData("return Api.Direct();", "Producer.FirstFailure")]
    [InlineData("return Api.Forward<int>(Rail.Fail<int>(new FirstFailure()));", "Producer.FirstFailure")]
    [InlineData("return Rail.Fail<int>(Api.Keep(new FirstFailure()));", "Producer.FirstFailure")]
    [InlineData("return Api.Create<SecondFailure>();", "Producer.SecondFailure")]
    [InlineData("return Api.Generic<string>();", "Producer.GenericFailure<string>")]
    [InlineData("return Container<int>.Create();", "Producer.GenericFailure<int>")]
    [InlineData("return Api.Partial(Rail.Fail<int>(new FirstFailure()));", "Producer.FirstFailure")]
    [InlineData("return Api.PartialGeneric<string>();", "Producer.GenericFailure<string>")]
    [InlineData("return Api.NestedRailValue();", "Producer.SecondFailure")]
    public async Task GeneratedSummaries_WorkFromReferenceAssemblies(string body, string expectedErrors)
    {
        var reference = EmitProducer(ProducerSource, true, out var generated);
        Assert.Contains("FlowAnalysisSummaryAttribute", generated);
        var diagnostic = Assert.Single(await AnalyzeConsumer(body, "RailFor<int>", false, reference));

        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Theory]
    [InlineData("return Api.Direct();", "Producer.FirstFailure")]
    [InlineData("return Api.Forward<int>(Rail.Fail<int>(new FirstFailure()));", "Producer.FirstFailure")]
    [InlineData("return Api.Generic<string>();", "Producer.GenericFailure<string>")]
    public async Task SourceProjectReferences_UseExportedSummariesWithoutOpeningForeignSyntaxTrees(
        string body,
        string expectedErrors)
    {
        _ = EmitProducer(ProducerSource, true, out var generated);
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp14);
        var producer = CSharpCompilation.Create("ProducerContracts",
            [CSharpSyntaxTree.ParseText(ProducerSource, parseOptions), CSharpSyntaxTree.ParseText(generated, parseOptions)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var reference = producer.ToMetadataReference();
        var diagnostic = Assert.Single(await AnalyzeConsumer(body, "RailFor<int>", false, reference));

        Assert.NotEmpty(producer.GetTypeByMetadataName("Producer.Api")!.DeclaringSyntaxReferences);
        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task SourceProjectWithoutSummaries_RemainsOpenEvenWhenItsBodyIsVisible()
    {
        var producer = CSharpCompilation.Create("ProducerContracts",
            [CSharpSyntaxTree.ParseText(ProducerSource, new CSharpParseOptions(LanguageVersion.CSharp14))],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var diagnostic = Assert.Single(await AnalyzeConsumer("return Api.Direct();", "RailFor<int>", false,
            producer.ToMetadataReference()));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("unavailable", diagnostic.Properties["Limitations"]);
    }

    [Theory]
    [InlineData("return Api.AsyncFactory();", "Task<RailFor<int>>", false)]
    [InlineData("return await Api.AsyncBody();", "Task<RailFor<int>>", true)]
    [InlineData("return await Api.Wrap<int>(Rail.Fail<int>(new SecondFailure()));", "ValueTask<RailFor<int>>", true)]
    public async Task GeneratedAsyncSummaries_PreserveTheAwaitedErrors(string body, string returnType, bool isAsync)
    {
        var reference = EmitProducer(ProducerSource, true, out _);
        var diagnostic = Assert.Single(await AnalyzeConsumer(body, returnType, isAsync, reference));

        Assert.Equal("Producer.SecondFailure", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task PrivateSummaryTypes_WidenTheMissingPartAndRetainAccessibleErrors()
    {
        var reference = EmitProducer(ProducerSource, true, out _);
        var diagnostic = Assert.Single(await AnalyzeConsumer("return Api.Hidden(flag);", "RailFor<int>", false, reference));

        Assert.Contains("Producer.SecondFailure", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("inaccessible", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task GeneratedOpenSummary_DoesNotBecomeClosedInTheConsumer()
    {
        var reference = EmitProducer(ProducerSource, true, out _);
        var diagnostic = Assert.Single(await AnalyzeConsumer("return Api.Open(reader);", "Task<RailFor<int>>", false,
            reference, "Producer.IReader reader"));

        Assert.Contains("Producer.FirstFailure", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task GeneratedConstructorCallbackSummary_RemainsConservativeInTheConsumer()
    {
        var reference = EmitProducer(ProducerSource, true, out _);
        var diagnostic = Assert.Single(await AnalyzeConsumer("return Api.ConstructorCallback();", "RailFor<int>", false,
            reference));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("effects", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task DisabledGeneration_LeavesExternalResultsOpen()
    {
        var reference = EmitProducer(ProducerSource, false, out var generated);
        Assert.Equal("", generated);
        var diagnostic = Assert.Single(await AnalyzeConsumer("return Api.Direct();", "RailFor<int>", false, reference));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Theory]
    [InlineData(999, "not a supported version")]
    [InlineData(1, "not base64")]
    [InlineData(1, "")]
    public async Task InvalidOrUnknownMetadata_RemainsOpenWithoutAnalyzerFailures(int version, string payload)
    {
        const string methodId = "M:Producer.Api.Direct~FlowOnRails.RailFor{System.Int32}";
        var source = $$"""
            using FlowOnRails;
            [assembly: FlowAnalysisSummaryAttribute({{version}}, "{{methodId}}", "{{payload}}")]
            namespace Producer;
            public static class Api
            {
                public static RailFor<int> Direct() => Rail.Ok(1);
            }
            """;
        var reference = EmitProducer(source, false, out _);
        var inspection = CSharpCompilation.Create("MetadataInspection", references: References.Add(reference));
        var declaredMethod = (IMethodSymbol)inspection.GetTypeByMetadataName("Producer.Api")!.GetMembers("Direct").Single();
        Assert.Equal(methodId, DocumentationCommentId.CreateDeclarationId(declaredMethod));
        var diagnostic = Assert.Single(await AnalyzeConsumer("return Api.Direct();", "RailFor<int>", false, reference));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("Summary metadata", diagnostic.Properties["Limitations"]);
    }

    private static MetadataReference EmitProducer(string source, bool enabled, out string generated)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp14);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create("ProducerContracts", [tree], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ErrorFlowGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            optionsProvider: new OptionsProvider(enabled));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        generated = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(generatedTree => generatedTree.ToString()));

        using var stream = new MemoryStream();
        var emission = outputCompilation.Emit(stream, options: new EmitOptions(metadataOnly: true, includePrivateMembers: false));
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static Task<Diagnostic[]> AnalyzeConsumer(
        string body,
        string returnType,
        bool isAsync,
        MetadataReference reference,
        string parameters = "bool flag")
    {
        var source = $$"""
            using FlowOnRails;
            using Producer;
            using System.Threading.Tasks;
            public static class Consumer
            {
                public static {{(isAsync ? "async " : "")}}{{returnType}} Subject({{parameters}})
                {
                    {{body}}
                }
            }
            """;

        return AnalyzeSource(source, true, false, "Subject", [reference]);
    }
}
