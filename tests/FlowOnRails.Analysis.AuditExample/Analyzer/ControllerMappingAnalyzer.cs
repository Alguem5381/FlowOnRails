using System.Collections.Immutable;
using System.Linq;

using FlowOnRails.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FlowOnRails.Analysis.AuditExample;

// The experiment's explicit contract: [AuditController] opts a class in;
// [MapsError(typeof(TError))] on that class covers TError and its subclasses.
// Only public ordinary actions returning Rail/Error or their Task/ValueTask wrappers
// participate. No assumption is made about routing or IActionResult conversion.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ControllerMappingAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor MissingMapping = new(
        "AUDIT001", "Known error needs a mapping",
        "Action '{0}' may return '{1}', which has no mapping on controller '{2}'",
        "ControllerMapping", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnknownMapping = new(
        "AUDIT002", "Keep a fallback for unknown errors",
        "Action '{0}' may return other errors; keep a general error fallback",
        "ControllerMapping", DiagnosticSeverity.Info, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [MissingMapping, UnknownMapping];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(startContext =>
        {
            var compilation = startContext.Compilation;
            var controllerAttribute = compilation.GetTypeByMetadataName("AuditControllerAttribute");
            var mappingAttribute = compilation.GetTypeByMetadataName("MapsErrorAttribute");

            if (controllerAttribute is null || mappingAttribute is null
                || !ErrorFlowAnalysis.TryCreate(compilation, out var analysis))
                return;

            startContext.RegisterSymbolAction(symbolContext =>
            {
                if (symbolContext.Symbol is not IMethodSymbol {
                        MethodKind: MethodKind.Ordinary,
                        DeclaredAccessibility: Accessibility.Public
                    } method ||
                    !method.ContainingType
                        .GetAttributes()
                        .Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, controllerAttribute)))
                    return;

                if (!analysis.TryAnalyzeMethod(method, out var summary, symbolContext.CancellationToken))
                    return;

                var mappings = method.ContainingType.GetAttributes()
                    .Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, mappingAttribute))
                    .Select(attribute => attribute.ConstructorArguments.FirstOrDefault().Value)
                    .OfType<INamedTypeSymbol>().ToArray();

                foreach (var knownType in summary.KnownErrorTypes)
                {
                    if (mappings.Any(mapping => Covers(mapping, knownType)))
                        continue;

                    symbolContext.ReportDiagnostic(Diagnostic.Create(MissingMapping,
                        method.Locations.FirstOrDefault(location => location.IsInSource),
                        method.Name, knownType.ToDisplayString(), method.ContainingType.Name)
                    );
                }

                // A mapped named family does not turn an open summary into an
                // exhaustive list. Preserve a separately observable fallback need.
                if (summary.HasUnknownErrors)
                    symbolContext.ReportDiagnostic(Diagnostic.Create(UnknownMapping,
                        method.Locations.FirstOrDefault(location => location.IsInSource), method.Name));
            }, SymbolKind.Method);
        });
    }

    private static bool Covers(ITypeSymbol mapping, ITypeSymbol errorType)
    {
        for (var candidate = errorType; candidate is not null; candidate = candidate.BaseType)
            if (SymbolEqualityComparer.Default.Equals(mapping, candidate))
                return true;

        return false;
    }
}
