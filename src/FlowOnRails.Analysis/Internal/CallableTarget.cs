using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;

namespace FlowOnRails.Analysis.Internal;

// A delegate type describes a signature, not a closed set of implementations.
// Targets are retained only when their identity is available in the current flow.
internal sealed class CallableTarget
{
    internal IMethodSymbol Method { get; }
    internal ControlFlowGraph? Graph { get; }
    internal TypeSubstitution Types { get; }
    internal bool IsVirtual { get; }

    internal CallableTarget(IMethodSymbol method, ControlFlowGraph? graph, TypeSubstitution types, bool isVirtual = false) =>
        (Method, Graph, Types, IsVirtual) = (method, graph, types, isVirtual);

    internal bool SameAs(CallableTarget other) =>
        SymbolEqualityComparer.Default.Equals(Method, other.Method)
        && Graph == other.Graph
        && Types.SameAs(other.Types)
        && IsVirtual == other.IsVirtual;
}
