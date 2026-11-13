using System;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FlowOnRails.Analysis.Internal;

internal static class ErrorSwitchAnalysis
{
    internal static ITypeSymbol[] MissingTypes(
        SyntaxNode switchNode,
        SemanticModel model,
        CancellationToken cancellationToken,
        MethodBodyResolver? methodBodies = null)
    {
        var expression = switchNode switch
        {
            SwitchStatementSyntax statement => statement.Expression,
            SwitchExpressionSyntax switchExpression => switchExpression.GoverningExpression,
            _ => null
        };

        if (expression is null
            || FlowQueries.ErrorsAt(model, expression, cancellationToken, methodBodies) is not { } errors
            || LibrarySymbols.Create(model.Compilation) is not { } symbols)
            return [];

        var patterns = switchNode switch
        {
            SwitchStatementSyntax statement => statement.Sections.SelectMany(section => section.Labels)
                .Where(label => label is CasePatternSwitchLabelSyntax or CaseSwitchLabelSyntax)
                .Select(label => label is CasePatternSwitchLabelSyntax patternLabel
                    ? ((SyntaxNode)patternLabel.Pattern, patternLabel.WhenClause)
                    : ((SyntaxNode)((CaseSwitchLabelSyntax)label).Value, null)),
            SwitchExpressionSyntax switchExpression => switchExpression.Arms.Select(arm => ((SyntaxNode)arm.Pattern, arm.WhenClause)),
            _ => Enumerable.Empty<(SyntaxNode Pattern, WhenClauseSyntax? WhenClause)>()
        };
        var existingPatterns = patterns.Select(entry => (Pattern: entry.Item1, WhenClause: entry.Item2)).ToArray();

        // Only claim coverage for unconditional type tests. Guards, property tests
        // and positional tests can reject an error even when its type matches.
        // Unknown negated/composite patterns are left alone instead of risking
        // an unreachable generated arm. The action never removes a fallback.
        if (existingPatterns.Any(entry => !IsSupportedPattern(entry.Pattern)))
            return [];

        var knownTypes = FlowQueries.KnownTypes(errors, symbols)
            .Where(type => model.GetEnclosingSymbol(expression.SpanStart, cancellationToken) is { } enclosingSymbol
                           && model.Compilation.IsSymbolAccessibleWithin(type, enclosingSymbol.ContainingType ?? (ISymbol)model.Compilation.Assembly)
                           && !existingPatterns.Any(entry => IsUnconditional(entry.WhenClause, model, cancellationToken)
                               && !IsFallback(entry.Pattern, model, cancellationToken)
                               && Covers(entry.Pattern, type, model, cancellationToken)))
            .OrderByDescending(InheritanceDepth)
            .ThenBy(type => type.ToDisplayString(), StringComparer.Ordinal).ToArray();

        return knownTypes;
    }

    internal static bool IsUnconditional(WhenClauseSyntax? whenClause, SemanticModel model, CancellationToken cancellationToken) =>
        whenClause is null || model.GetConstantValue(whenClause.Condition, cancellationToken).Value is true;

    private static bool IsSupportedPattern(SyntaxNode pattern) => pattern is
        TypePatternSyntax or DeclarationPatternSyntax or DiscardPatternSyntax or VarPatternSyntax
        or ConstantPatternSyntax or RecursivePatternSyntax or ExpressionSyntax;

    private static bool Covers(SyntaxNode pattern, ITypeSymbol type, SemanticModel model, CancellationToken cancellationToken)
    {
        var testedType = pattern switch
        {
            TypePatternSyntax typePattern => typePattern.Type,
            ConstantPatternSyntax constant => constant.Expression,
            ExpressionSyntax expression => expression,
            DeclarationPatternSyntax declaration => declaration.Type,
            RecursivePatternSyntax { PropertyPatternClause: null or { Subpatterns.Count: 0 }, PositionalPatternClause: null } recursive => recursive.Type,
            _ => null
        };

        if (testedType is null || model.GetTypeInfo(testedType, cancellationToken).Type is not { } coveredType)
            return false;

        // Pattern coverage follows the language's reference conversions, including
        // implemented interfaces and generic variance. User-defined conversions
        // cannot make a runtime type pattern cover an otherwise unrelated error.
        var conversion = model.Compilation.ClassifyCommonConversion(type, coveredType);

        return conversion.IsIdentity || conversion.IsImplicit && conversion.IsReference;
    }

    private static int InheritanceDepth(ITypeSymbol type)
    {
        var depth = 0;

        for (var baseType = (type as INamedTypeSymbol)?.BaseType; baseType is not null; baseType = baseType.BaseType)
            depth++;

        return depth;
    }

    internal static bool IsFallback(SyntaxNode pattern, SemanticModel model, CancellationToken cancellationToken)
    {
        if (pattern is DiscardPatternSyntax or VarPatternSyntax
            or RecursivePatternSyntax { Type: null, PropertyPatternClause.Subpatterns.Count: 0, PositionalPatternClause: null })
            return true;

        var error = model.Compilation.GetTypeByMetadataName("FlowOnRails.Error");

        return error is not null && Covers(pattern, error, model, cancellationToken);
    }

}
