using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using FlowOnRails.Analysis;
using FlowOnRails.Analysis.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ErrorFlowAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Summary = new(
        "FORFLOW001",
        "Possible returned errors",
        "{0}",
        "FlowAnalysis",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Experimental analysis of returned errors, including bounded concrete calls in the compilation. "
                     + "Enable through FlowOnRailsEnableFlowAnalysis; interface contracts remain open."
    );

    private static readonly DiagnosticDescriptor InvocationSummary = new(
        "FORFLOW002",
        "Possible errors returned by this call",
        "{0}",
        "FlowAnalysis",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Experimental error flow at a method call, before subsequent recovery in the caller. "
                     + "Enable through FlowOnRailsEnableFlowAnalysis; interface contracts remain open."
    );

    private static readonly DiagnosticDescriptor CompleteSwitch = new(
        "FORFLOW003",
        "Add cases for known errors",
        "Known error types can be handled explicitly in this switch",
        "FlowAnalysis",
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: true,
        description: "Offers cases for known errors while retaining other cases and open error alternatives.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Summary, InvocationSummary, CompleteSwitch];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            if (!compilationContext.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(
                    "build_property.FlowOnRailsEnableFlowAnalysis", out var enabled)
                || !string.Equals(enabled, "true", System.StringComparison.OrdinalIgnoreCase))
                return;

            var librarySymbols = LibrarySymbols.Create(compilationContext.Compilation);

            if (librarySymbols is null)
                return;

            var methodBodies = new MethodBodyResolver(compilationContext.Compilation);
            compilationContext.RegisterSyntaxNodeAction(syntaxContext =>
            {
                if (ErrorSwitchAnalysis.MissingTypes(syntaxContext.Node, syntaxContext.SemanticModel,
                        syntaxContext.CancellationToken, methodBodies).Length == 0)
                    return;

                var keyword = syntaxContext.Node is SwitchStatementSyntax statement
                    ? statement.SwitchKeyword
                    : ((SwitchExpressionSyntax)syntaxContext.Node).SwitchKeyword;
                syntaxContext.ReportDiagnostic(Diagnostic.Create(CompleteSwitch, keyword.GetLocation()));
            }, SyntaxKind.SwitchStatement, SyntaxKind.SwitchExpression);
            compilationContext.RegisterOperationBlockAction(operationContext =>
                AnalyzeBlock(operationContext, librarySymbols, methodBodies));
            compilationContext.RegisterSymbolAction(symbolContext =>
                AnalyzeInterfaceMethod(symbolContext, librarySymbols), SymbolKind.Method);
        });
    }

    private static void AnalyzeBlock(
        OperationBlockAnalysisContext operationContext,
        LibrarySymbols librarySymbols,
        MethodBodyResolver methodBodies)
    {
        var methodSymbol = operationContext.OwningSymbol as IMethodSymbol;
        var operationBlock = operationContext.OperationBlocks.FirstOrDefault();

        if (operationBlock is null)
            return;

        var reportDeclaration = methodSymbol is { MethodKind: MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation }
                                && methodSymbol.ContainingType.TypeKind != TypeKind.Interface
                                && IsRailReturn(methodSymbol.ReturnType, librarySymbols);
        var invocations = operationBlock.DescendantsAndSelf()
            .OfType<IInvocationOperation>()
            .Where(invocation => !invocation.IsImplicit && IsRailReturn(invocation.Type, librarySymbols))
            .ToArray();

        if (!reportDeclaration && invocations.Length == 0)
            return;

        var invocationResults = new Dictionary<IInvocationOperation, MethodFlowResult>();

        if (methodSymbol is not null)
        {
            var controlFlowGraph = operationContext.GetControlFlowGraph(operationBlock);
            var analysisResult = MethodFlowAnalysis.Analyze(
                librarySymbols, methodBodies, controlFlowGraph, methodSymbol, operationContext.CancellationToken,
                invocationResults
            );

            if (reportDeclaration)
                operationContext.ReportDiagnostic(CreateSummaryDiagnostic(
                    Summary, methodSymbol.Locations.FirstOrDefault(location => location.IsInSource),
                    methodSymbol.Name, "Declaration", analysisResult, librarySymbols
                ));
        }

        var resultsBySpan = invocationResults.ToDictionary(entry => entry.Key.Syntax.Span, entry => entry.Value);

        foreach (var invocation in invocations)
        {
            if (invocation.Syntax is not InvocationExpressionSyntax invocationSyntax)
                continue;

            var isSuccessFactory = invocation.TargetMethod.Name == "Ok"
                                   && librarySymbols.IsRail(invocation.TargetMethod.ContainingType);
            var successValueIsError = isSuccessFactory
                                      && invocation.TargetMethod.TypeArguments.Length == 1
                                      && librarySymbols.IsError(invocation.TargetMethod.TypeArguments[0]);

            if (isSuccessFactory && !successValueIsError)
                continue;

            if (!resultsBySpan.TryGetValue(invocation.Syntax.Span, out var invocationResult))
                // Unsupported regions and unevaluated callback bodies still get
                // an explicitly open Info; missing observations never mean success.
                invocationResult = new MethodFlowResult(
                    librarySymbols.OpenContract(invocation.TargetMethod),
                    false,
                    [new ErrorFlowLimitation(ErrorFlowLimitationCode.CallSiteUnavailable,
                        "Call-site flow is unavailable in this context")]
                );

            var name = invocationSyntax.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
                MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
                SimpleNameSyntax simpleName => simpleName,
                _ => null
            };
            var location = name?.Identifier.GetLocation() ?? invocationSyntax.Expression.GetLocation();

            operationContext.ReportDiagnostic(CreateSummaryDiagnostic(
                InvocationSummary, location, invocation.TargetMethod.Name, "Invocation", invocationResult, librarySymbols,
                successValueIsError
            ));
        }
    }

    private static void AnalyzeInterfaceMethod(SymbolAnalysisContext symbolContext, LibrarySymbols librarySymbols)
    {
        if (symbolContext.Symbol is not IMethodSymbol { MethodKind: MethodKind.Ordinary } methodSymbol
            || methodSymbol.ContainingType.TypeKind != TypeKind.Interface
            || !(librarySymbols.IsRail(methodSymbol.ReturnType) || librarySymbols.IsAwaitableRail(methodSymbol.ReturnType)))
            return;

        var analysisResult = new MethodFlowResult(
            librarySymbols.OpenContract(methodSymbol),
            false,
            [new ErrorFlowLimitation(ErrorFlowLimitationCode.OpenDispatch,
                "Interface contracts may return additional errors")]
        );

        symbolContext.ReportDiagnostic(CreateSummaryDiagnostic(
            Summary, methodSymbol.Locations.FirstOrDefault(location => location.IsInSource),
            methodSymbol.Name, "Declaration", analysisResult, librarySymbols
        ));
    }

    private static bool IsRailReturn(ITypeSymbol? type, LibrarySymbols symbols) =>
        symbols.IsRail(type) || type is not null && symbols.IsAwaitableRail(type);

    private static Diagnostic CreateSummaryDiagnostic(
        DiagnosticDescriptor descriptor,
        Location? location,
        string methodName,
        string targetKind,
        MethodFlowResult analysisResult,
        LibrarySymbols librarySymbols,
        bool successValueIsError = false)
    {
        var returnedValue = analysisResult.ReturnValue.UnwrapAwaitables();

        if (returnedValue.IsBottom)
            returnedValue = FlowValue.Rail(ErrorSet.Empty, false);
        var errorNames = string.Join(", ", returnedValue.Errors.DisplayNames());
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("SuccessValueIsError", successValueIsError.ToString())
            .Add("MethodName", methodName)
            .Add("TargetKind", targetKind)
            .Add("ErrorTypes", errorNames)
            .Add("HasUnknownErrors", returnedValue.Errors.IsOpen.ToString())
            .Add("HasUnresolvedConditions", analysisResult.UnresolvedConditions.ToString())
            .Add("MaySucceed", returnedValue.MaySucceed.ToString())
            .Add("MayBeInvalid", returnedValue.MayBeInvalid.ToString())
            .Add("Limitations", string.Join("; ", analysisResult.Limitations.Select(limitation => limitation.Message).Distinct()));

        return Diagnostic.Create(
            descriptor,
            location,
            properties,
            successValueIsError
                ? "This call creates a successful rail with an Error as its value. The Error is carried as a success value, not as a returned failure."
                : ErrorFlowMessage.Format(analysisResult, librarySymbols, targetKind == "Invocation")
        );
    }
}
