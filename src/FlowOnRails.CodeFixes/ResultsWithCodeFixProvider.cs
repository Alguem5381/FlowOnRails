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
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

namespace FlowOnRails.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ResultsWithCodeFixProvider)), Shared]
public sealed class ResultsWithCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ["FORFLOW001"];

    // Each implementation can edit several contract documents. A batch fix could
    // overwrite another implementation's additions, so fixes are offered one at a time.
    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var cancellationToken = context.CancellationToken;
        var root = await context.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        var declaration = root?.FindNode(context.Span).FirstAncestorOrSelf<MethodDeclarationSyntax>();

        if (model is null || declaration is null
            || model.GetDeclaredSymbol(declaration, cancellationToken) is not { } implementation
            || implementation.ContainingType.TypeKind == TypeKind.Interface
            || FlowQueries.ReturnedErrors(model, implementation, cancellationToken) is not { } errors
            || LibrarySymbols.Create(model.Compilation) is not { } symbols)
            return;

        var knownTypes = FlowQueries.KnownTypes(errors, symbols).Where(type => !FlowQueries.ContainsTypeParameter(type)).ToArray();
        var changes = new Dictionary<DocumentId, List<(MethodDeclarationSyntax Declaration, ITypeSymbol[] Types)>>();

        // Resolve the contract in its own project before choosing types. Visibility
        // from the implementation does not imply a reference from the contract's layer.
        foreach (var contractType in implementation.ContainingType.AllInterfaces)
        foreach (var contractMethod in contractType.GetMembers().OfType<IMethodSymbol>())
        {
            if (!SymbolEqualityComparer.Default.Equals(
                    implementation.ContainingType.FindImplementationForInterfaceMember(contractMethod)?.OriginalDefinition,
                    implementation.OriginalDefinition))
                continue;

            var sourceMethod = await SymbolFinder.FindSourceDefinitionAsync(
                contractMethod.OriginalDefinition, context.Document.Project.Solution, cancellationToken).ConfigureAwait(false);
            var syntax = sourceMethod?.DeclaringSyntaxReferences.FirstOrDefault();

            if (syntax is null
                || await syntax.GetSyntaxAsync(cancellationToken).ConfigureAwait(false) is not MethodDeclarationSyntax contractDeclaration
                || context.Document.Project.Solution.GetDocument(syntax.SyntaxTree) is not { } contractDocument)
                continue;

            var contractModel = await contractDocument.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            if (contractModel is null
                || contractModel.GetDeclaredSymbol(contractDeclaration, cancellationToken) is not { } contractSymbol
                || LibrarySymbols.Create(contractModel.Compilation) is not { } contractSymbols)
                continue;

            var declaredErrors = contractSymbols.OpenContract(contractSymbol).UnwrapAwaitables().Errors;
            var availableTypes = new List<ITypeSymbol>();

            foreach (var knownType in knownTypes)
            {
                var referenceId = DocumentationCommentId.CreateReferenceId(knownType);
                var availableType = referenceId is null ? null
                    : DocumentationCommentId.GetFirstSymbolForReferenceId(referenceId, contractModel.Compilation) as ITypeSymbol;

                if (availableType is null
                    || FlowQueries.TypeIdentity(availableType) != FlowQueries.TypeIdentity(knownType)
                    || !contractSymbols.IsError(availableType)
                    || !contractModel.Compilation.IsSymbolAccessibleWithin(availableType, contractSymbol.ContainingType)
                    || declaredErrors.Cases.Any(errorCase =>
                        !SymbolEqualityComparer.Default.Equals(errorCase.Type, contractSymbols.Error)
                        && ErrorSet.IsSubtype(availableType, errorCase.Type)))
                    continue;

                availableTypes.Add(availableType);
            }

            if (availableTypes.Count == 0)
                continue;

            if (!changes.TryGetValue(contractDocument.Id, out var documentChanges))
                changes.Add(contractDocument.Id, documentChanges = []);

            if (!documentChanges.Any(change => change.Declaration.Span == contractDeclaration.Span))
                documentChanges.Add((contractDeclaration, availableTypes.ToArray()));
        }

        if (changes.Count == 0)
            return;

        context.RegisterCodeFix(CodeAction.Create(
            "Declare known errors on interface contracts (ResultsWith)",
            cancellation => ApplyAsync(context.Document.Project.Solution, changes, cancellation),
            nameof(ResultsWithCodeFixProvider)), context.Diagnostics);
    }

    private static async Task<Solution> ApplyAsync(
        Solution solution,
        Dictionary<DocumentId, List<(MethodDeclarationSyntax Declaration, ITypeSymbol[] Types)>> changes,
        CancellationToken cancellationToken)
    {
        foreach (var documentChanges in changes)
        {
            var document = solution.GetDocument(documentChanges.Key)!;
            var root = (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false))!;
            var replacements = documentChanges.Value.ToDictionary(change => change.Declaration.Span, change => change.Types);
            var updatedRoot = root.ReplaceNodes(documentChanges.Value.Select(change => change.Declaration), (original, rewritten) =>
            {
                var attributes = replacements[original.Span]
                    .OrderBy(type => type.ToDisplayString(), System.StringComparer.Ordinal)
                    .Select(type =>
                    {
                        var attributeName = SyntaxFactory.ParseName("global::FlowOnRails.ResultsWith<"
                            + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">");
                        var attribute = SyntaxFactory.Attribute(attributeName);

                        return SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));
                    });

                return rewritten.AddAttributeLists(attributes.ToArray())
                    .WithAdditionalAnnotations(Formatter.Annotation, Simplifier.Annotation);
            });

            solution = solution.WithDocumentSyntaxRoot(document.Id, updatedRoot);
        }

        return solution;
    }
}
