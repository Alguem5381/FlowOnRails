using FlowOnRails;
using FlowOnRails.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

const string source = """
    using FlowOnRails;
    public sealed record Missing : Error;
    public interface IReader { [ResultsWith<Missing>] RailFor<int> Read(); }
    public sealed class Reader : IReader { public RailFor<int> Read() => new Missing(); }
    """;
var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Append(typeof(Rail).Assembly.Location).Distinct()
    .Select(path => MetadataReference.CreateFromFile(path));
var compilation = CSharpCompilation.Create("ToolInput", [CSharpSyntaxTree.ParseText(source)], references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
if (!ErrorFlowAnalysis.TryCreate(compilation, out var analysis))
    throw new InvalidOperationException("The packaged API must create a context for a FlowOnRails compilation.");
var method = compilation.GetTypeByMetadataName("Reader")!.GetMembers("Read").OfType<IMethodSymbol>().Single();
var contract = compilation.GetTypeByMetadataName("IReader")!.GetMembers("Read").OfType<IMethodSymbol>().Single();

if (!analysis.CanAnalyze(method) || !analysis.CanAnalyze(contract))
    throw new InvalidOperationException("The packaged API must recognize supported concrete and interface methods.");

if (!analysis.TryAnalyzeMethod(method, out var result)
    || !analysis.TryAnalyzeMethod(contract, out var contractResult))
    throw new InvalidOperationException("Supported queries must return summaries.");

if (result.HasUnknownErrors || result.KnownErrorTypes.Single().Name != "Missing"
    || !contractResult.HasUnknownErrors
    || result.Origin != ErrorFlowSummaryOrigin.SourceBody
    || contractResult.Origin != ErrorFlowSummaryOrigin.OpenContract
    || contractResult.LimitationDetails.Single().Code != ErrorFlowLimitationCode.OpenDispatch)
    throw new InvalidOperationException("The packaged public analysis API returned an unexpected summary.");

if (typeof(ErrorFlowAnalysis).Assembly.GetReferencedAssemblies().Any(assembly =>
        assembly.Name == "FlowOnRails" || assembly.Name!.Contains("Workspaces")))
    throw new InvalidOperationException("The analysis assembly must remain independent of runtime and Workspaces.");

Console.WriteLine("Packaged public analysis API passed without an IDE or Workspaces.");

#if NET11_0
var modernSource = "using System; using System.Collections.Generic; using System.Threading.Tasks;\n"
    + File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Input.cs.txt"));
var modernCompilation = CSharpCompilation.Create("ModernInput",
    [CSharpSyntaxTree.ParseText(modernSource, new CSharpParseOptions(LanguageVersion.CSharp15))], references,
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
var compilerErrors = modernCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();

if (compilerErrors.Length != 0)
    throw new InvalidOperationException(string.Join("\n", compilerErrors.Select(diagnostic => diagnostic.ToString())));

var modernAnalysis = new ErrorFlowAnalysis(modernCompilation);
Dictionary<string, string[]> expectedErrors = new()
{
    ["UnionSuccess"] = [],
    ["UnionFailure"] = ["Missing"],
    ["ClosedHierarchy"] = ["Denied", "Missing"],
    ["CollectionArguments"] = ["Denied", "Missing"],
    ["CollectionArgumentEffects"] = ["Missing"],
    ["LabeledBreak"] = ["Missing"],
    ["LabeledContinue"] = ["Denied", "Missing"],
    ["ExtensionIndexer"] = ["Missing"],
    ["TaskFailure"] = ["Missing"],
    ["ValueTaskFailure"] = ["Denied"],
    ["TaskBind"] = ["Missing"],
    ["ValueTaskMapError"] = ["Denied"]
};

foreach (var (methodName, expected) in expectedErrors)
{
    var modernMethod = modernCompilation.GetTypeByMetadataName("ModernStudy")!.GetMembers(methodName).OfType<IMethodSymbol>().Single();
    var modernSummary = modernAnalysis.AnalyzeMethod(modernMethod);
    var actual = modernSummary.KnownErrorTypes.Select(type => type.Name).Order(StringComparer.Ordinal).ToArray();

    if (modernSummary.HasUnknownErrors || !actual.SequenceEqual(expected))
        throw new InvalidOperationException($"Unexpected C# 15 API summary for {methodName}: {string.Join(", ", actual)}; "
            + $"unknown: {modernSummary.HasUnknownErrors}; {string.Join("; ", modernSummary.Limitations)}");
}

var modernContract = modernCompilation.GetTypeByMetadataName("IModernReader")!.GetMembers("Read").OfType<IMethodSymbol>().Single();
var modernContractSummary = modernAnalysis.AnalyzeMethod(modernContract);

if (!modernContractSummary.HasUnknownErrors
    || modernContractSummary.Origin != ErrorFlowSummaryOrigin.OpenContract
    || modernContractSummary.KnownErrorTypes.Single().Name != "DomainFailure")
    throw new InvalidOperationException("A closed error family must not close an interface contract.");

Console.WriteLine($"Packaged API: {expectedErrors.Count} C# 15 summaries passed on Roslyn {typeof(CSharpCompilation).Assembly.GetName().Version}.");
#endif
