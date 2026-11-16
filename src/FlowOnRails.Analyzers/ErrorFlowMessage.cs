using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using FlowOnRails.Analysis.Internal;
using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analyzers;

internal static class ErrorFlowMessage
{
    internal static string Format(MethodFlowResult result, LibrarySymbols symbols, bool isCall)
    {
        var returnedValue = result.ReturnValue.UnwrapAwaitables();
        var namespaceTree = new NamespaceNode();
        var unknownAlternatives = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var errorCase in returnedValue.Errors.Cases)
        {
            // The open Error family represents missing knowledge, not another
            // useful concrete type. Keep exclusions as alternatives: exclusions
            // from different families cannot safely be combined into one list.
            if (!errorCase.IsExact && SymbolEqualityComparer.Default.Equals(errorCase.Type, symbols.Error))
            {
                if (!errorCase.Excluded.IsEmpty)
                    unknownAlternatives.Add("Other errors except " + string.Join(", ", errorCase.Excluded
                        .Select(type => type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                        .OrderBy(name => name, StringComparer.Ordinal)));

                continue;
            }

            var namespaceName = errorCase.Type.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
                ? containingNamespace.ToDisplayString()
                : "";
            var errorName = errorCase.GetDisplayName();

            // Remove only the outer namespace. Generic arguments, containing
            // types and excluded families retain their qualified identities.
            if (namespaceName.Length > 0 && errorName.StartsWith(namespaceName + ".", StringComparison.Ordinal))
                errorName = errorName.Substring(namespaceName.Length + 1);

            var currentNode = namespaceTree;

            if (namespaceName.Length > 0)
                foreach (var namespacePart in namespaceName.Split('.'))
                {
                    if (!currentNode.Children.TryGetValue(namespacePart, out var childNode))
                    {
                        childNode = new NamespaceNode();
                        currentNode.Children.Add(namespacePart, childNode);
                    }

                    currentNode = childNode;
                }

            currentNode.Errors.Add(errorName);
        }

        var message = new StringBuilder();

        var subject = isCall ? "call" : "method";
        var hasNamedErrors = namespaceTree.Children.Count > 0 || namespaceTree.Errors.Count > 0;

        if (returnedValue.IsBottom)
            message.Append("No normal result was identified for this ").Append(subject).Append(" in the supported flow.");
        else if (hasNamedErrors)
        {
            message.Append("This ").Append(subject).Append(" may return errors:\n\n");
            AppendNamespace(message, namespaceTree, 0);
        }
        else
            message.Append(returnedValue.Errors.IsOpen
                ? "No known error types were identified for this "
                : "No returned errors were identified for this ").Append(subject).Append('.');

        if (returnedValue.Errors.IsOpen)
            message.Append(hasNamedErrors ? "\n" : "\n\n").Append("Other error types may be returned.");
        else if (hasNamedErrors)
            message.Append("\nNo additional error types were found in the supported flow.");

        if (unknownAlternatives.Count > 0)
        {
            message.Append("\n\nUnknown error alternatives:");

            foreach (var alternative in unknownAlternatives)
                message.Append("\n    ").Append(alternative);
        }

        if (result.UnresolvedConditions)
            message.Append("\n\nSome conditions could not be determined, so the list of possible errors may be broader than necessary.");

        if (returnedValue.MayBeInvalid)
            message.Append(returnedValue.MaySucceed || !returnedValue.Errors.IsEmpty
                ? "\n\nAn uninitialized rail may also be returned."
                : "\n\nAn uninitialized rail may be returned.");

        if (!result.Limitations.IsEmpty)
        {
            message.Append("\n\nAnalysis limits:");

            foreach (var limitation in result.Limitations.Select(detail => detail.Message).Distinct()
                         .OrderBy(messageText => messageText, StringComparer.Ordinal))
                message.Append("\n    ").Append(limitation.TrimEnd('.')).Append('.');
        }

        return message.ToString();
    }

    // Collapse single-child namespace chains, stopping at a type or a branch.
    // Traversing symbols' namespaces rather than splitting type names preserves
    // dots inside generic arguments and nested-type names as part of the leaf.
    private static void AppendNamespace(StringBuilder message, NamespaceNode node, int indentation)
    {
        foreach (var errorName in node.Errors)
            message.Append(' ', indentation).Append(errorName).Append('\n');

        foreach (var child in node.Children)
        {
            var namespaceName = child.Key;
            var childNode = child.Value;

            while (childNode.Errors.Count == 0 && childNode.Children.Count == 1)
            {
                var descendant = childNode.Children.First();
                namespaceName += "." + descendant.Key;
                childNode = descendant.Value;
            }

            message.Append(' ', indentation).Append(namespaceName).Append(":\n");
            AppendNamespace(message, childNode, indentation + 4);
        }
    }

    private sealed class NamespaceNode
    {
        internal SortedDictionary<string, NamespaceNode> Children { get; } = new(StringComparer.Ordinal);
        internal SortedSet<string> Errors { get; } = new(StringComparer.Ordinal);
    }
}
