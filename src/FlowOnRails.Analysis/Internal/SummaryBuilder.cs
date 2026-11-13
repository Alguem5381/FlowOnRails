using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.Analysis.Internal;

internal static class SummaryBuilder
{
    // Export a bounded, complete partition of abstract inputs, not a sampled set of
    // concrete executions. Every combination remains covered; when splitting would
    // exceed the budget, retain the unsplit input. Consumers join all overlapping
    // alternatives and substitute argument dependencies within each alternative.
    internal static MethodFlowResult Analyze(
        LibrarySymbols symbols,
        MethodBodyResolver bodies,
        ControlFlowGraph graph,
        IMethodSymbol method,
        CancellationToken cancellationToken)
    {
        var summary = MethodFlowAnalysis.Analyze(symbols, bodies, graph, method, cancellationToken);

        if (graph.Blocks.Length > 128 || method.Parameters.IsEmpty)
            return summary;

        var testedTypes = graph.Blocks.SelectMany(block => block.BranchValue is { } branch
                ? block.Operations.Add(branch) : block.Operations)
            .SelectMany(operation => operation.DescendantsAndSelf())
            .Select(operation => operation switch
            {
                ITypePatternOperation pattern => pattern.MatchedType,
                IDeclarationPatternOperation pattern => pattern.MatchedType,
                IIsTypeOperation test => test.TypeOperand,
                _ => null
            })
            .Where(type => type is not null && symbols.IsError(type))
            .Distinct<ITypeSymbol?>(SymbolEqualityComparer.Default).Take(3).ToArray();
        List<ImmutableArray<FlowValue>> inputs =
        [
            [.. method.Parameters.Select(parameter => symbols.Unknown(parameter.Type))]
        ];

        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind == RefKind.Out)
                continue;

            var unknown = symbols.Unknown(parameter.Type);
            var alternatives = new List<FlowValue>();

            if (unknown.Kind == ValueKind.Boolean)
                alternatives.AddRange([FlowValue.Bool(true), FlowValue.Bool(false)]);
            else if (unknown.Kind == ValueKind.Rail)
                alternatives.AddRange([
                    FlowValue.Rail(ErrorSet.Empty, true),
                    FlowValue.Rail(unknown.Errors, false),
                    FlowValue.Rail(ErrorSet.Empty, false, true)]);
            else if (unknown.Kind == ValueKind.Error)
            {
                var families = new List<ErrorSet> { unknown.Errors };

                foreach (var type in testedTypes)
                    families = families.SelectMany(errors => new[] { errors.Restrict(type!, true), errors.Restrict(type!, false) })
                        .Where(errors => !errors.IsEmpty).ToList();

                alternatives.AddRange(families.Select(errors => FlowValue.Error(errors)));
                alternatives.Add(FlowValue.Null);
            }

            if (alternatives.Count < 2 || inputs.Count * alternatives.Count > 16)
                continue;

            inputs = inputs.SelectMany(input => alternatives.Select(value => input.SetItem(parameter.Ordinal, value))).ToList();
        }

        if (inputs.Count == 1)
            return summary;

        var variants = inputs.Select(input => new GuardedSummary(input,
            MethodFlowAnalysis.Analyze(symbols, bodies, graph, method, cancellationToken, initialArguments: [.. input])));

        return new MethodFlowResult(summary.ReturnValue, summary.UnresolvedConditions, summary.Limitations,
            summary.ParameterOutputs, [.. variants]);
    }
}
