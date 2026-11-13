using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analysis.Internal;

// Operations belong to the generic declaration's graph. Substitute its type
// parameters at each use; never cache a graph rewritten for one constructed call.
internal sealed class TypeSubstitution
{
    private readonly Compilation _compilation;
    private readonly Dictionary<ITypeParameterSymbol, ITypeSymbol> _arguments = new(SymbolEqualityComparer.Default);

    internal TypeSubstitution(Compilation compilation, IMethodSymbol methodSymbol, TypeSubstitution? enclosing = null)
    {
        _compilation = compilation;

        if (enclosing is not null)
            foreach (var argument in enclosing._arguments)
                _arguments[argument.Key] = argument.Value;

        foreach (var parameter in methodSymbol.OriginalDefinition.TypeParameters)
            _arguments[parameter] = Apply(methodSymbol.TypeArguments[parameter.Ordinal])!;

        if (methodSymbol.OriginalDefinition.PartialImplementationPart is { } implementation)
            foreach (var parameter in implementation.TypeParameters)
                _arguments[parameter] = methodSymbol.TypeArguments[parameter.Ordinal];

        for (var containingType = methodSymbol.ContainingType;
             containingType is not null;
             containingType = containingType.ContainingType)
            foreach (var parameter in containingType.OriginalDefinition.TypeParameters)
                _arguments[parameter] = Apply(containingType.TypeArguments[parameter.Ordinal])!;
    }

    internal ITypeSymbol? Apply(ITypeSymbol? type)
    {
        if (type is ITypeParameterSymbol parameter)
            return _arguments.TryGetValue(parameter, out var argument) ? argument : parameter;

        if (type is IArrayTypeSymbol array)
            return _compilation.CreateArrayTypeSymbol(Apply(array.ElementType)!, array.Rank);

        if (type is not INamedTypeSymbol namedType || namedType.IsUnboundGenericType)
            return type;

        var definition = namedType.OriginalDefinition;

        if (namedType.ContainingType is { } containingType)
        {
            var constructedContainer = (INamedTypeSymbol)Apply(containingType)!;
            definition = constructedContainer.GetTypeMembers(namedType.Name, namedType.Arity)
                .First(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition,
                    namedType.OriginalDefinition));
        }

        var substituted = namedType.Arity == 0
            ? definition
            : definition.Construct(namedType.TypeArguments.Select(argument => Apply(argument)!).ToArray());

        // Extension declarations have a synthetic containing type rather than a
        // value type. Newer Roslyn hosts reject WithNullableAnnotation on that
        // symbol, even when asked to keep its existing annotation unchanged.
        return substituted.NullableAnnotation == type.NullableAnnotation
            ? substituted
            : substituted.WithNullableAnnotation(type.NullableAnnotation);
    }

    internal IMethodSymbol Apply(IMethodSymbol methodSymbol)
    {
        var containingType = (INamedTypeSymbol)Apply(methodSymbol.ContainingType)!;
        var definition = methodSymbol.AssociatedSymbol is IPropertySymbol property
            ? containingType.GetMembers(property.Name).OfType<IPropertySymbol>()
                .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition,
                    property.OriginalDefinition))?.GetMethod
            : containingType.GetMembers(methodSymbol.Name).OfType<IMethodSymbol>()
            .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition,
                methodSymbol.OriginalDefinition));
        definition ??= methodSymbol.OriginalDefinition;

        return methodSymbol.IsGenericMethod
            ? definition.Construct(methodSymbol.TypeArguments.Select(argument => Apply(argument)!).ToArray())
            : definition;
    }

    internal bool SameAs(TypeSubstitution other) =>
        _arguments.Count == other._arguments.Count
        && _arguments.All(argument => other._arguments.TryGetValue(argument.Key, out var otherType)
            && SymbolEqualityComparer.Default.Equals(argument.Value, otherType));
}
