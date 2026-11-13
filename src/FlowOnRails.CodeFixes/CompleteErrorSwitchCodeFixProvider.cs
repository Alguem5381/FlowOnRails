using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FlowOnRails.Analysis.Internal;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace FlowOnRails.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CompleteErrorSwitchCodeFixProvider)), Shared]
public sealed class CompleteErrorSwitchCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ["FORFLOW003"];

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var switchNode = root?.FindNode(context.Span).AncestorsAndSelf()
            .FirstOrDefault(node => node is SwitchStatementSyntax or SwitchExpressionSyntax);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

        if (switchNode is null || model is null)
            return;

        var knownTypes = ErrorSwitchAnalysis.MissingTypes(switchNode, model, context.CancellationToken);

        if (knownTypes.Length == 0)
            return;

        context.RegisterCodeFix(CodeAction.Create(
            "Add cases for known errors (keep other cases)",
            cancellation => AddCasesAsync(context.Document, switchNode, knownTypes, model, cancellation),
            nameof(CompleteErrorSwitchCodeFixProvider)), context.Diagnostics);
    }

    private static async Task<Document> AddCasesAsync(
        Document document,
        SyntaxNode switchNode,
        IReadOnlyList<ITypeSymbol> knownTypes,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
        SyntaxNode updatedSwitch;

        if (switchNode is SwitchExpressionSyntax expression)
        {
            var insertionIndex = expression.Arms.TakeWhile(arm =>
                !ErrorSwitchAnalysis.IsUnconditional(arm.WhenClause, model, cancellationToken)
                || !ErrorSwitchAnalysis.IsFallback(arm.Pattern, model, cancellationToken)).Count();
            var arms = knownTypes.Select(type => SyntaxFactory.SwitchExpressionArm(
                CreateErrorPattern(type),
                SyntaxFactory.ThrowExpression(CreatePlaceholderException())));
            updatedSwitch = expression.WithArms(expression.Arms.InsertRange(insertionIndex, arms));
        }
        else
        {
            var statement = (SwitchStatementSyntax)switchNode;
            // A default label can appear before specific sections. Keep every
            // existing specific pattern ahead of newly added base-type cases.
            var insertionIndex = statement.Sections.Count;

            if (statement.Sections.LastOrDefault() is { } lastSection
                && lastSection.Labels.Any(label => label is DefaultSwitchLabelSyntax
                    || label is CasePatternSwitchLabelSyntax patternLabel
                    && ErrorSwitchAnalysis.IsUnconditional(patternLabel.WhenClause, model, cancellationToken)
                    && ErrorSwitchAnalysis.IsFallback(patternLabel.Pattern, model, cancellationToken)))
                insertionIndex--;

            var sections = knownTypes.Select(type => SyntaxFactory.SwitchSection(
                SyntaxFactory.SingletonList<SwitchLabelSyntax>(SyntaxFactory.CasePatternSwitchLabel(
                    CreateErrorPattern(type),
                    SyntaxFactory.Token(SyntaxKind.ColonToken))),
                SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.ThrowStatement(CreatePlaceholderException()))));
            updatedSwitch = statement.WithSections(statement.Sections.InsertRange(insertionIndex, sections));
        }

        return document.WithSyntaxRoot(root.ReplaceNode(switchNode,
            updatedSwitch.WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation)));
    }

    private static TypePatternSyntax CreateErrorPattern(ITypeSymbol type)
    {
        var typeName = SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .WithAdditionalAnnotations(Simplifier.Annotation);

        return SyntaxFactory.TypePattern(typeName);
    }

    private static ObjectCreationExpressionSyntax CreatePlaceholderException()
    {
        var exceptionType = SyntaxFactory.ParseTypeName("global::System.NotImplementedException")
            .WithAdditionalAnnotations(Simplifier.Annotation);

        return SyntaxFactory.ObjectCreationExpression(exceptionType, SyntaxFactory.ArgumentList(), null);
    }
}
