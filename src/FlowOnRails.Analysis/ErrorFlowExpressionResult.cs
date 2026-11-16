using System.Collections.Immutable;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FlowOnRails.Analysis;

/// <summary>The result of querying one expression within its executable body.</summary>
public sealed class ErrorFlowExpressionResult
{
    /// <summary>Gets the requested expression.</summary>
    public ExpressionSyntax Expression { get; }

    /// <summary>Gets whether the expression was analyzed, unreachable or unsupported.</summary>
    public ErrorFlowExpressionStatus Status { get; }

    /// <summary>Gets a summary only when <see cref="Status"/> is <see cref="ErrorFlowExpressionStatus.Analyzed"/>.</summary>
    /// <remarks>
    /// Error alternatives belong to this point. Conditions and limitations conservatively
    /// describe the containing body and may include operations outside the expression.
    /// </remarks>
    public ErrorFlowSummary? Summary { get; }

    /// <summary>Gets classified limits, including the reason an expression is unsupported.</summary>
    public ImmutableArray<ErrorFlowLimitation> LimitationDetails { get; }

    internal ErrorFlowExpressionResult(
        ExpressionSyntax expression,
        ErrorFlowExpressionStatus status,
        ErrorFlowSummary? summary,
        ImmutableArray<ErrorFlowLimitation> limitations = default)
    {
        Expression = expression;
        Status = status;
        Summary = summary;
        LimitationDetails = summary?.LimitationDetails ?? (limitations.IsDefault ? [] : limitations);
    }
}
