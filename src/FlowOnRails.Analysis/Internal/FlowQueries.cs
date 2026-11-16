using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.Analysis.Internal;

internal static class FlowQueries
{
    internal static ErrorSet? ReturnedErrors(SemanticModel model, IMethodSymbol method, CancellationToken cancellationToken)
    {
        var symbols = LibrarySymbols.Create(model.Compilation);

        if (symbols is null)
            return null;

        var bodies = new MethodBodyResolver(model.Compilation);
        var graph = bodies.GetGraph(method, cancellationToken);

        return graph is null ? null : MethodFlowAnalysis.Analyze(
            symbols, bodies, graph, method, cancellationToken).ReturnValue.UnwrapAwaitables().Errors;
    }

    internal static ErrorSet? ErrorsAt(
        SemanticModel model,
        ExpressionSyntax expression,
        CancellationToken cancellationToken,
        MethodBodyResolver? methodBodies = null)
    {
        var symbols = LibrarySymbols.Create(model.Compilation);

        if (symbols is null || !symbols.IsError(model.GetTypeInfo(expression, cancellationToken).Type))
            return null;

        var declaration = expression.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
        var method = declaration is null
            ? model.GetEnclosingSymbol(expression.SpanStart, cancellationToken) as IMethodSymbol
            : model.GetDeclaredSymbol(declaration, cancellationToken) as IMethodSymbol;

        if (method is null)
            return null;

        var bodies = methodBodies ?? new MethodBodyResolver(model.Compilation);
        var graph = bodies.GetGraph(method, cancellationToken);

        if (graph is null && expression.Ancestors().OfType<GlobalStatementSyntax>().Any())
        {
            var operation = model.GetOperation(expression, cancellationToken);

            while (operation?.Parent is { } parent)
                operation = parent;

            graph = operation switch
            {
                IMethodBodyOperation methodBody => ControlFlowGraph.Create(methodBody, cancellationToken),
                IBlockOperation block => ControlFlowGraph.Create(block, cancellationToken),
                _ => null
            };
        }

        if (graph is null)
            return null;

        var observation = new FlowObservation(expression);
        MethodFlowAnalysis.Analyze(symbols, bodies, graph, method, cancellationToken, observation: observation);

        return observation.Value.IsBottom ? null : observation.Value.Errors;
    }

    internal static IEnumerable<ITypeSymbol> KnownTypes(ErrorSet errors, LibrarySymbols symbols) =>
        errors.Cases.Select(errorCase => errorCase.Type)
            .Where(type => !SymbolEqualityComparer.Default.Equals(type, symbols.Error))
            .Distinct<ITypeSymbol>(SymbolEqualityComparer.Default);

    // Attribute type arguments cannot contain a method/type parameter, including
    // one nested in a constructed generic type. A fix must remain compilable.
    internal static bool ContainsTypeParameter(ITypeSymbol type) =>
        type is ITypeParameterSymbol
        || type is IArrayTypeSymbol array && ContainsTypeParameter(array.ElementType)
        || type is INamedTypeSymbol named && (named.TypeArguments.Any(ContainsTypeParameter)
            || named.ContainingType is { } containing && ContainsTypeParameter(containing));

    internal static string TypeIdentity(ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "|" + type.ContainingAssembly?.Identity
        + (type is INamedTypeSymbol named ? "[" + string.Join(",", named.TypeArguments.Select(TypeIdentity)) + "]" : "");
}
