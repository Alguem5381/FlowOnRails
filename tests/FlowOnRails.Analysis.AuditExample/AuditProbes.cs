using System.Collections.Immutable;
using System.Reflection;

using FlowOnRails;
using FlowOnRails.Analyzers;
using FlowOnRails.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FlowOnRails.Analysis.AuditExample;

internal static class AuditProbes
{
    internal static async Task RunAsync(ImmutableArray<MetadataReference> references, CSharpParseOptions parseOptions)
    {
        const string source = """
            using FlowOnRails;
            using System.Threading.Tasks;
            public sealed record Before : Error;
            public sealed record After : Error;
            public sealed class Box { public int Value { get; set; } }
            public static class Probes
            {
                private static Box Change(ref RailFor<int> rail)
                {
                    rail = new After();
                    return new Box();
                }
                public static RailFor<int> AssignmentReceiver()
                {
                    RailFor<int> rail = new Before();
                    Change(ref rail).Value = 1;
                    return rail;
                }
                public static RailFor<int> AssignmentReceiverControl()
                {
                    RailFor<int> rail = new Before();
                    var box = Change(ref rail);
                    box.Value = 1;
                    return rail;
                }
                public static Task<RailFor<int>> NullTask() => null!;
                public static RailFor<int> GenericPattern<TError>() where TError : Error
                {
                    Error error = new Before();
                    return error is TError ? new After() : new Before();
                }
                public static RailFor<int> GenericPatternControl() => GenericPattern<Before>();
            }
            """;
        var compilation = CSharpCompilation.Create("AuditProbes", [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        RequireCompiles(compilation);
        var analysis = new ErrorFlowAnalysis(compilation);
        var receiver = analysis.AnalyzeMethod(Method(compilation, "Probes", "AssignmentReceiver"));
        var control = analysis.AnalyzeMethod(Method(compilation, "Probes", "AssignmentReceiverControl"));
        Console.WriteLine($"PROBE assignment receiver: {Describe(receiver)}; control: {Describe(control)}");

        using var stream = new MemoryStream();
        var emission = compilation.Emit(stream);
        Require(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        var runtimeAssembly = Assembly.Load(stream.ToArray());
        var runtimeRail = (RailFor<int>)runtimeAssembly.GetType("Probes")!.GetMethod("AssignmentReceiver")!.Invoke(null, null)!;
        runtimeRail.TryUnwrap(out _, out var runtimeError);
        Require(runtimeError?.GetType().Name == "After", "runtime assignment returns After");
        Require(receiver.KnownErrorTypes.Single().Name == "After" && !receiver.HasUnknownErrors,
            "assignment receiver must preserve ref effects");
        Require(control.KnownErrorTypes.Single().Name == "After" && !control.HasUnknownErrors, "control tracks ref effect");

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ErrorFlowGenerator().AsSourceGenerator()],
            parseOptions: parseOptions, optionsProvider: new EnabledOptionsProvider());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);
        using var metadataStream = new MemoryStream();
        Require(generated.Emit(metadataStream).Success, "export probes");
        var consumer = CSharpCompilation.Create("ProbeConsumer", references: references.Add(MetadataReference.CreateFromImage(metadataStream.ToArray())));
        var external = new ErrorFlowAnalysis(consumer).AnalyzeMethod(Method(consumer, "Probes", "AssignmentReceiver"));
        Require(external.KnownErrorTypes.Single().Name == "After" && !external.HasUnknownErrors,
            "external receiver summary must preserve ref effects");
        Console.WriteLine($"PASS assignment receiver: runtime={runtimeError!.GetType().Name}; local={Describe(receiver)}; external={Describe(external)}");

        var genericDefinition = Method(compilation, "Probes", "GenericPattern");
        var genericSummary = analysis.AnalyzeMethod(genericDefinition);
        var specializedSource = analysis.AnalyzeMethod(Method(compilation, "Probes", "GenericPatternControl"));
        var externalGeneric = Method(consumer, "Probes", "GenericPattern")
            .Construct(consumer.GetTypeByMetadataName("Before")!);
        var specializedMetadata = new ErrorFlowAnalysis(consumer).AnalyzeMethod(externalGeneric);
        var runtimeGeneric = runtimeAssembly.GetType("Probes")!.GetMethod("GenericPattern")!
            .MakeGenericMethod(runtimeAssembly.GetType("Before")!);
        var runtimeGenericRail = (RailFor<int>)runtimeGeneric.Invoke(null, null)!;
        runtimeGenericRail.TryUnwrap(out _, out var runtimeGenericError);
        Require(runtimeGenericError?.GetType().Name == "After" && specializedSource.KnownErrorTypes.Single().Name == "After",
            "generic pattern runtime and source call return After");
        Require(genericSummary.KnownErrorTypes.Any(type => type.Name == "After")
                && genericSummary.KnownErrorTypes.Any(type => type.Name == "Before")
                && specializedMetadata.KnownErrorTypes.Any(type => type.Name == "After"),
            "generic pattern summaries must retain every viable alternative");
        Console.WriteLine($"PASS generic pattern: runtime={runtimeGenericError?.GetType().Name}; definition={Describe(genericSummary)}; source call={Describe(specializedSource)}; metadata={Describe(specializedMetadata)}");

        var nullTask = analysis.AnalyzeMethod(Method(compilation, "Probes", "NullTask"));
        Require(runtimeAssembly.GetType("Probes")!.GetMethod("NullTask")!.Invoke(null, null) is null, "runtime returns a null Task");
        Require(!nullTask.MayProduceResult && !nullTask.MayReturnNull && !nullTask.HasUnknownErrors,
            "null Task cannot produce an awaited result");
        var externalNullTask = new ErrorFlowAnalysis(consumer).AnalyzeMethod(Method(consumer, "Probes", "NullTask"));
        Require(!externalNullTask.MayProduceResult && !externalNullTask.MayReturnNull,
            "null Task metadata must preserve the absence of an awaited result");
        Console.WriteLine($"PASS null task: local={Describe(nullTask)}; external={Describe(externalNullTask)}");
        var crashingConsumer = consumer.AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            using System;
            using System.Threading.Tasks;
            using FlowOnRails;
            public sealed class AuditControllerAttribute : Attribute { }
            public sealed class MapsErrorAttribute(Type errorType) : Attribute { }
            [AuditController]
            public sealed class CrashController
            {
                public Task<RailFor<int>> Read() => Probes.NullTask();
            }
            """, parseOptions)).WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        RequireCompiles(crashingConsumer);
        var crashDiagnostics = await crashingConsumer.WithAnalyzers([new ControllerMappingAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        Require(!crashDiagnostics.Any(diagnostic => diagnostic.Id == "AD0001"), "real analyzer host must not crash on null Task metadata");
        Console.WriteLine("PASS satellite host accepts producer-generated NullTask summary without AD0001.");
        await CheckInterfaceSwitchAsync(references, parseOptions);
    }

    private static async Task CheckInterfaceSwitchAsync(ImmutableArray<MetadataReference> references, CSharpParseOptions parseOptions)
    {
        const string source = """
            using FlowOnRails;
            public interface IRetryable { }
            public sealed record Missing : Error, IRetryable;
            public sealed record Other : Error;
            public static class SwitchProbe
            {
                public static string Describe(bool flag)
                {
                    Error error = flag ? new Missing() : new Other();
                    return error switch { IRetryable => "retry", _ => "fallback" };
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("SwitchProbe", LanguageNames.CSharp)
            .WithParseOptions(parseOptions)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(references);
        var document = project.AddDocument("Switch.cs", source);
        var compilation = (await document.Project.GetCompilationAsync())!;
        RequireCompiles(compilation);
        var options = new AnalyzerOptions([], new EnabledOptionsProvider());
        var diagnostics = await compilation.WithAnalyzers([new ErrorFlowAnalyzer()], options).GetAnalyzerDiagnosticsAsync();
        var diagnostic = diagnostics.Single(diagnostic => diagnostic.Id == "FORFLOW003");
        var actions = new List<CodeAction>();
        var provider = new CompleteErrorSwitchCodeFixProvider();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action), CancellationToken.None));
        var operations = await actions.Single().GetOperationsAsync(CancellationToken.None);
        var updated = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        var updatedCompilation = (await updated.Project.GetCompilationAsync())!;
        var errors = updatedCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Require(errors.Length == 0, "switch code fix must preserve compilable interface coverage");
        var updatedText = (await updated.GetTextAsync()).ToString();
        Require(updatedText.Contains("Other =>") && !updatedText.Contains("Missing =>"),
            "only errors not covered by the interface should be added");
        Console.WriteLine("PASS switch code fix preserves interface coverage without CS8510.");
        Console.WriteLine(updatedText);
    }

    private static IMethodSymbol Method(Compilation compilation, string typeName, string methodName) =>
        compilation.GetTypeByMetadataName(typeName)!.GetMembers(methodName).OfType<IMethodSymbol>().Single();

    private static string Describe(ErrorFlowSummary summary) =>
        $"known=[{string.Join(", ", summary.KnownErrorTypes.Select(type => type.Name))}], open={summary.HasUnknownErrors}, result={summary.MayProduceResult}, invalid={summary.MayBeInvalid}";

    private static void RequireCompiles(Compilation compilation) =>
        Require(!compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            string.Join(Environment.NewLine, compilation.GetDiagnostics()));

    private static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
    }
}
