using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace FlowOnRails.Analysis.Internal;

// Observations belong to one root frame. Calls still specialize their returns,
// but their bodies cannot contribute evaluations from another lexical scope.
internal sealed class FlowExpressionObservations
{
    private readonly Dictionary<TextSpan, List<ExpressionObservation>> _expressions = [];
    private readonly TypeSubstitution _types;

    internal FlowExpressionObservations(TypeSubstitution types) => _types = types;

    internal void Add(ExpressionObservation observation)
    {
        if (observation.Operation is not { } operation)
            return;

        if (!_expressions.TryGetValue(operation.Syntax.Span, out var entries))
            _expressions.Add(operation.Syntax.Span, entries = []);

        entries.Add(observation);
    }

    internal void Register(IOperation operation)
    {
        foreach (var observation in Matching(operation))
            observation.IsRepresented = true;
    }

    internal void Observe(IOperation operation, FlowValue value, LibrarySymbols symbols)
    {
        foreach (var observation in Matching(operation))
        {
            observation.WasEvaluated = true;
            observation.Value = observation.Value.Join(symbols.MatchesReturnType(observation.Type!, value)
                ? value : symbols.Unknown(observation.Type));
        }
    }

    private IEnumerable<ExpressionObservation> Matching(IOperation operation)
    {
        if (!_expressions.TryGetValue(operation.Syntax.Span, out var observations))
            yield break;

        var type = _types.Apply(operation.Type ?? (operation as IFlowCaptureOperation)?.Value.Type);

        foreach (var observation in observations)
            if (operation.Syntax.SyntaxTree == observation.Expression.SyntaxTree
                && SymbolEqualityComparer.Default.Equals(type, observation.Type))
                yield return observation;
    }
}

internal sealed class ExpressionObservation
{
    internal ExpressionSyntax Expression { get; }
    internal IOperation? Operation { get; }
    internal ITypeSymbol? Type { get; }
    internal bool IsRepresented { get; set; }
    internal bool WasEvaluated { get; set; }
    internal FlowValue Value { get; set; } = FlowValue.Bottom;

    internal ExpressionObservation(ExpressionSyntax expression, IOperation? operation, ITypeSymbol? type) =>
        (Expression, Operation, Type) = (expression, operation, type);
}
