using System.Collections.Concurrent;
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
        methodSymbol = methodSymbol.OriginalDefinition;
        methodSymbol = methodSymbol.PartialImplementationPart ?? methodSymbol;

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

            var controlFlowGraph = operation switch
            {
                IMethodBodyOperation methodBody => ControlFlowGraph.Create(methodBody, cancellationToken),
                IBlockOperation { Parent: null } block => ControlFlowGraph.Create(block, cancellationToken),
                _ => null
            };

            if (controlFlowGraph is null)
                continue;

            return _graphs.GetOrAdd(methodSymbol, controlFlowGraph);
        }

        return null;
    }
}
