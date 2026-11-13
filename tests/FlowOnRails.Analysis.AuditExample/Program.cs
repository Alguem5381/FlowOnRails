using System.Collections.Immutable;

using FlowOnRails;
using FlowOnRails.Analysis;
using FlowOnRails.Analysis.AuditExample;
using FlowOnRails.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;

var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Select(path => MetadataReference.CreateFromFile(path)).Cast<MetadataReference>()
    .Append(MetadataReference.CreateFromFile(typeof(Rail).Assembly.Location)).ToImmutableArray();
var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp14);
var producer = CreateCompilation("AuditProducer", """
    using FlowOnRails;
    using System.Threading.Tasks;
    namespace Producer;
    public sealed record RemoteMissing : Error;
    public static class Handler
    {
        public static RailFor<int> Read() => new RemoteMissing();
        public static Task<RailFor<int>> ReadTask() => Task.FromResult(Read());
        public static ValueTask<RailFor<int>> ReadValueTask() => new(Read());
    }
    """);
GeneratorDriver driver = CSharpGeneratorDriver.Create(
    [new ErrorFlowGenerator().AsSourceGenerator()], parseOptions: parseOptions,
    optionsProvider: new EnabledOptionsProvider());
driver = driver.RunGeneratorsAndUpdateCompilation(producer, out var generatedProducer, out var generatorDiagnostics);
Require(!generatorDiagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), "generator diagnostics");
Require(driver.GetRunResult().GeneratedTrees.Length > 0, "producer exported summaries");
using var referenceStream = new MemoryStream();
var emission = generatedProducer.Emit(referenceStream, options: new EmitOptions(metadataOnly: true, includePrivateMembers: false));
Require(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
var producerReference = MetadataReference.CreateFromImage(referenceStream.ToArray());

var consumer = CreateCompilation("AuditConsumer", """
    using System;
    using System.Threading.Tasks;
    using FlowOnRails;
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AuditControllerAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class MapsErrorAttribute(Type errorType) : Attribute { }
    public sealed record Missing : Error;
    public sealed record Denied : Error;
    public interface IHandler
    {
        [ResultsWith<Missing>]
        Task<RailFor<int>> Read();
    }
    [AuditController, MapsError(typeof(Missing))]
    public sealed class ExampleController
    {
        public RailFor<int> Mapped() => new Missing();
        public RailFor<int> Unmapped() => new Denied();
        public RailFor<int> Recovered() => Rail.Fail<int>(new Denied()).Recover(error => 1);
        public RailFor<Error> SuccessError() => Rail.Ok<Error>(new Denied());
        public Task<RailFor<int>> Open(IHandler handler) => handler.Read();
        public Task<RailFor<int>> LocalTask() => Task.FromResult(Rail.Fail<int>(new Denied()));
        public async ValueTask<RailFor<int>> LocalValueTask() { await Task.Yield(); return new Denied(); }
        public RailFor<int> Remote() => Producer.Handler.Read();
        public Task<RailFor<int>> RemoteTask() => Producer.Handler.ReadTask();
        public ValueTask<RailFor<int>> RemoteValueTask() => Producer.Handler.ReadValueTask();
        public string OutsideScope() => "not an action in this experiment";
    }
    [AuditController, MapsError(typeof(Producer.RemoteMissing))]
    public sealed class MappedRemoteController
    {
        public Task<RailFor<int>> Read() => Producer.Handler.ReadTask();
    }
    """, producerReference);
var analyzerDiagnostics = await consumer.WithAnalyzers([new ControllerMappingAnalyzer()]).GetAnalyzerDiagnosticsAsync();
Require(!analyzerDiagnostics.Any(diagnostic => diagnostic.Id == "AD0001"), "no analyzer host failures");
var expectedUnmappedActions = new[] { "LocalTask", "LocalValueTask", "Remote", "RemoteTask", "RemoteValueTask", "Unmapped" };
var actualUnmappedActions = analyzerDiagnostics.Where(diagnostic => diagnostic.Id == "AUDIT001")
    .Select(diagnostic => consumer.SyntaxTrees.Single().GetText().ToString(diagnostic.Location.SourceSpan))
    .OrderBy(name => name, StringComparer.Ordinal).ToArray();
Require(actualUnmappedActions.SequenceEqual(expectedUnmappedActions),
    "unmapped actions: " + string.Join(", ", actualUnmappedActions));
Require(analyzerDiagnostics.Count(diagnostic => diagnostic.Id == "AUDIT002") == 1, "open contract keeps fallback");
foreach (var diagnostic in analyzerDiagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start))
    Console.WriteLine(diagnostic);
Console.WriteLine("PASS: concurrent DiagnosticAnalyzer host, six missing mappings, one open contract, mapped/recovered/success-Error cases clean.");

var analysis = new ErrorFlowAnalysis(consumer);
foreach (var action in expectedUnmappedActions.Append("Mapped").Append("Recovered").Append("SuccessError").Append("Open"))
{
    var summary = analysis.AnalyzeMethod(Method(consumer, "ExampleController", action));
    Console.WriteLine($"SUMMARY {action}: known=[{string.Join(", ", summary.KnownErrorTypes.Select(type => type.Name))}], open={summary.HasUnknownErrors}, success={summary.MaySucceed}, result={summary.MayProduceResult}");
}

await AuditProbes.RunAsync(references, parseOptions);

CSharpCompilation CreateCompilation(string name, string source, MetadataReference? extraReference = null)
{
    var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source, parseOptions)],
        extraReference is null ? references : references.Add(extraReference),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    Require(!compilation.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
        string.Join(Environment.NewLine, compilation.GetDiagnostics()));

    return compilation;
}

static IMethodSymbol Method(Compilation compilation, string typeName, string methodName) =>
    compilation.GetTypeByMetadataName(typeName)!.GetMembers(methodName).OfType<IMethodSymbol>().Single();

static void Require(bool condition, string description)
{
    if (!condition)
        throw new InvalidOperationException(description);
}

internal sealed class EnabledOptionsProvider : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new EnabledOptions();
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
}

internal sealed class EnabledOptions : AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, out string value)
    {
        value = "true";

        return key == "build_property.FlowOnRailsEnableFlowAnalysis";
    }
}
