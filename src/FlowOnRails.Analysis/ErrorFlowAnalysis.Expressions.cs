using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

using FlowOnRails.Analysis.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.Analysis;

public sealed partial class ErrorFlowAnalysis
{
    /// <summary>Queries values of several expressions during one analysis of their containing body.</summary>
    /// <remarks>
    /// Accepts methods, constructors, local functions and anonymous functions, regardless of
    /// their return type. Every expression must belong to that same executable body in this
    /// compilation. Nested functions have their own bodies and must be queried separately.
    /// The body is interpreted with unknown parameters and captured inputs. Calls within it
    /// retain supported argument specialization. Results preserve input order and duplicates.
    /// Only Rail, Error and their supported Task/ValueTask wrappers have value summaries.
    /// Unsupported expressions do not disable other points. No result means neither success
    /// nor absence of errors; inspect each point's status before using its summary.
    /// Conditions and limitations conservatively describe the entire body. Values are specific
    /// to each expression and join all observed evaluations, including repeated loop visits.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The body or expression sequence is null.</exception>
    /// <exception cref="ArgumentException">A symbol or expression is foreign, null, or belongs to another body.</exception>
    /// <exception cref="OperationCanceledException">The query was canceled.</exception>
    public ImmutableArray<ErrorFlowExpressionResult> AnalyzeExpressions(
        IMethodSymbol body,
        IEnumerable<ExpressionSyntax> expressions,
        CancellationToken cancellationToken = default)
    {
        if (body is null)
            throw new ArgumentNullException(nameof(body));

        if (expressions is null)
            throw new ArgumentNullException(nameof(expressions));

        if (!SymbolEqualityComparer.Default.Equals(body.ContainingAssembly, _symbols.Compilation.Assembly))
            throw new ArgumentException("Use a body symbol from this compilation.", nameof(body));

        cancellationToken.ThrowIfCancellationRequested();

        var definition = MethodBodyResolver.GetDefinition(body);
        var types = new TypeSubstitution(_symbols.Compilation, body);
        var observations = new List<ExpressionObservation>();
        var observer = new FlowExpressionObservations(types);

        foreach (var expression in expressions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (expression is null || !_symbols.Compilation.ContainsSyntaxTree(expression.SyntaxTree))
                throw new ArgumentException("Use non-null expressions from this compilation.", nameof(expressions));

            var model = _symbols.Compilation.GetSemanticModel(expression.SyntaxTree);
            var owner = model.GetEnclosingSymbol(expression.SpanStart, cancellationToken) as IMethodSymbol;

            if (owner is null || !SymbolEqualityComparer.Default.Equals(MethodBodyResolver.GetDefinition(owner), definition))
                throw new ArgumentException("Every expression must belong to the supplied executable body.", nameof(expressions));

            var valueExpression = expression;

            while (valueExpression is ParenthesizedExpressionSyntax parenthesized)
                valueExpression = parenthesized.Expression;

            var operation = model.GetOperation(valueExpression, cancellationToken);
            var typeInfo = model.GetTypeInfo(expression, cancellationToken);
            var type = types.Apply(typeInfo.Type ?? typeInfo.ConvertedType);

            if (type is null || !_symbols.IsTrackedReturn(type) || IsWriteOnly(operation))
                operation = null;

            var observation = new ExpressionObservation(expression, operation, type);
            observations.Add(observation);
            observer.Add(observation);
        }

        if (observations.Count == 0)
            return [];

        var graph = _bodies.GetGraph(body, cancellationToken);

        if (graph is null)
            return [.. observations.Select(observation => Unsupported(observation.Expression,
                [new ErrorFlowLimitation(ErrorFlowLimitationCode.BodyUnavailable,
                    "The executable body is unavailable for expression analysis")]))];

        foreach (var operation in GraphOperations(graph, cancellationToken))
            observer.Register(operation);

        var result = MethodFlowAnalysis.Analyze(_symbols, _bodies, graph, body, cancellationToken,
            expressionObservations: observer);
        var incomplete = result.Limitations.Any(limitation => limitation.Code is
            ErrorFlowLimitationCode.AnalysisBudgetExceeded or ErrorFlowLimitationCode.RecursionDidNotConverge);
        var results = ImmutableArray.CreateBuilder<ErrorFlowExpressionResult>(observations.Count);

        foreach (var observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (observation.Operation is null || !observation.IsRepresented)
            {
                results.Add(Unsupported(observation.Expression,
                    [new ErrorFlowLimitation(ErrorFlowLimitationCode.UnsupportedOperation,
                        "This expression's value is not represented by supported flow analysis")]));
                continue;
            }

            if (!observation.WasEvaluated)
            {
                results.Add(result.Limitations.IsEmpty
                    ? new ErrorFlowExpressionResult(observation.Expression, ErrorFlowExpressionStatus.Unreachable, null)
                    : Unsupported(observation.Expression, [.. result.Limitations,
                        new ErrorFlowLimitation(ErrorFlowLimitationCode.CallSiteUnavailable,
                            "The expression was not evaluated and the containing body has analysis limitations")]));
                continue;
            }

            // An unfinished traversal cannot close a point's alternatives, even if
            // an earlier visit reached it before the shared budget was exhausted.
            var value = incomplete ? observation.Value.Join(_symbols.Unknown(observation.Type)) : observation.Value;
            var summary = new ErrorFlowSummary(new MethodFlowResult(value, result.UnresolvedConditions, result.Limitations),
                _symbols, ErrorFlowSummaryOrigin.SourceBody);
            results.Add(new ErrorFlowExpressionResult(observation.Expression, ErrorFlowExpressionStatus.Analyzed, summary));
        }

        return results.MoveToImmutable();
    }

    private static ErrorFlowExpressionResult Unsupported(
        ExpressionSyntax expression,
        ImmutableArray<ErrorFlowLimitation> limitations) =>
        new(expression, ErrorFlowExpressionStatus.Unsupported, null, limitations);

    private static bool IsWriteOnly(IOperation? operation)
    {
        while (operation?.Parent is ITupleOperation or IDeclarationExpressionOperation)
            operation = operation.Parent;

        return operation?.Parent switch
        {
            ISimpleAssignmentOperation assignment when assignment.Target == operation => true,
            IDeconstructionAssignmentOperation assignment when assignment.Target == operation => true,
            IArgumentOperation { Parameter.RefKind: RefKind.Out } => true,
            _ => false
        };
    }

    private static IEnumerable<IOperation> GraphOperations(ControlFlowGraph graph, CancellationToken cancellationToken)
    {
        foreach (var block in graph.Blocks)
        {
            var roots = block.BranchValue is { } branchValue ? block.Operations.Add(branchValue) : block.Operations;

            foreach (var root in roots)
            foreach (var operation in Operations(root, cancellationToken))
                yield return operation;
        }
    }

    private static IEnumerable<IOperation> Operations(IOperation operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return operation;

        foreach (var child in operation.ChildOperations)
        foreach (var descendant in Operations(child, cancellationToken))
            yield return descendant;
    }
}
