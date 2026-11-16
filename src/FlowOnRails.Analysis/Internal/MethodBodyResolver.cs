using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.Analysis.Internal;

// Cache only immutable method graphs. Returned values depend on the caller's
// arguments and must not be reused as context-free summaries of another call.
internal sealed class MethodBodyResolver
{
    private readonly Compilation _compilation;
    internal MetadataSummaries Metadata { get; }
    private readonly ConcurrentDictionary<IMethodSymbol, ControlFlowGraph> _graphs =
        new(SymbolEqualityComparer.Default);

    internal MethodBodyResolver(Compilation compilation)
    {
        _compilation = compilation;
        Metadata = new MetadataSummaries(LibrarySymbols.Create(compilation)!);
    }

    internal ControlFlowGraph? GetGraph(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
    {
        methodSymbol = GetDefinition(methodSymbol);

        // IDE workspaces can expose another project's source symbols through a
        // CompilationReference. Syntax references do not make that body local:
        // foreign assemblies must use their exported summaries, just as DLLs do.
        if (methodSymbol.IsExtern
            || methodSymbol.IsAbstract
            || !SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingAssembly, _compilation.Assembly))
            return null;

        if (_graphs.TryGetValue(methodSymbol, out var existingGraph))
            return existingGraph;

        foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var declaration = syntaxReference.GetSyntax(cancellationToken);

            if (!_compilation.ContainsSyntaxTree(declaration.SyntaxTree))
                continue;

            var semanticModel = _compilation.GetSemanticModel(declaration.SyntaxTree);

            var operation = semanticModel.GetOperation(declaration, cancellationToken);

            if (declaration is CompilationUnitSyntax unit
                && unit.Members.OfType<GlobalStatementSyntax>().FirstOrDefault() is { } globalStatement)
                operation = semanticModel.GetOperation(globalStatement.Statement, cancellationToken);

            if (declaration is PropertyDeclarationSyntax { ExpressionBody.Expression: { } expression })
            {
                operation = semanticModel.GetOperation(expression, cancellationToken);

                while (operation?.Parent is { } parent)
                    operation = parent;
            }

            if (declaration is IndexerDeclarationSyntax { ExpressionBody.Expression: { } indexerExpression })
            {
                operation = semanticModel.GetOperation(indexerExpression, cancellationToken);

                while (operation?.Parent is { } parent)
                    operation = parent;
            }

            var nestedFunctions = new Stack<IMethodSymbol>();

            while (operation is not null)
            {
                if (operation is IAnonymousFunctionOperation anonymousFunction)
                    nestedFunctions.Push(anonymousFunction.Symbol);
                else if (operation is ILocalFunctionOperation localFunction)
                    nestedFunctions.Push(localFunction.Symbol);

                if (operation.Parent is null)
                    break;

                operation = operation.Parent;
            }

            var controlFlowGraph = operation switch
            {
                IMethodBodyOperation methodBody => ControlFlowGraph.Create(methodBody, cancellationToken),
                IConstructorBodyOperation constructorBody => ControlFlowGraph.Create(constructorBody, cancellationToken),
                IBlockOperation { Parent: null } block => ControlFlowGraph.Create(block, cancellationToken),
                IFieldInitializerOperation field => ControlFlowGraph.Create(field, cancellationToken),
                IPropertyInitializerOperation property => ControlFlowGraph.Create(property, cancellationToken),
                _ => null
            };

            while (controlFlowGraph is not null && nestedFunctions.Count > 0)
                controlFlowGraph = GetNestedGraph(controlFlowGraph, nestedFunctions.Pop(), cancellationToken);

            if (controlFlowGraph is null)
                continue;

            return _graphs.GetOrAdd(methodSymbol, controlFlowGraph);
        }

        return null;
    }

    internal static IMethodSymbol GetDefinition(IMethodSymbol method) =>
        method.OriginalDefinition.PartialImplementationPart ?? method.OriginalDefinition;

    private static ControlFlowGraph? GetNestedGraph(
        ControlFlowGraph graph,
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        foreach (var localFunction in graph.LocalFunctions)
            if (SymbolEqualityComparer.Default.Equals(localFunction, method))
                return graph.GetLocalFunctionControlFlowGraph(localFunction, cancellationToken);

        foreach (var block in graph.Blocks)
        {
            var operations = block.BranchValue is { } branchValue
                ? block.Operations.Add(branchValue) : block.Operations;

            foreach (var operation in operations)
            {
                var lambda = FindLambda(operation, method, cancellationToken);

                if (lambda is not null)
                    return graph.GetAnonymousFunctionControlFlowGraph(lambda, cancellationToken);
            }
        }

        return null;
    }

    private static IFlowAnonymousFunctionOperation? FindLambda(
        IOperation operation,
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (operation is IFlowAnonymousFunctionOperation lambda)
            return SymbolEqualityComparer.Default.Equals(lambda.Symbol, method) ? lambda : null;

        foreach (var child in operation.ChildOperations)
            if (FindLambda(child, method, cancellationToken) is { } nested)
                return nested;

        return null;
    }
}
