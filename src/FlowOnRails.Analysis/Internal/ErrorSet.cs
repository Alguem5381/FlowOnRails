using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analysis.Internal;

/// <summary>An upper bound, not a claim that every represented error will occur.</summary>
internal sealed class ErrorSet
{
    internal static readonly ErrorSet Empty = new(ImmutableArray<ErrorCase>.Empty);

    internal ImmutableArray<ErrorCase> Cases { get; }
    internal bool IsEmpty => Cases.IsEmpty;
    internal bool IsOpen => Cases.Any(errorCase => !errorCase.IsExact);

    internal ErrorSet(ImmutableArray<ErrorCase> cases) => Cases = cases;

    internal static ErrorSet Of(ITypeSymbol errorType, bool exact) =>
        new([new ErrorCase(errorType, exact || errorType.IsSealed, [])]);

    internal ErrorSet Union(ErrorSet other)
    {
        var errorCases = Cases.ToBuilder();

        foreach (var errorCase in other.Cases)
        {
            if (errorCases.Any(existingCase => existingCase.SameAs(errorCase)))
                continue;

            errorCases.Add(errorCase);
        }

        return new(errorCases.ToImmutable());
    }

    internal bool SameAs(ErrorSet other) =>
        Cases.Length == other.Cases.Length
        && Cases.All(errorCase => other.Cases.Any(errorCase.SameAs));

    internal ErrorSet Intersect(ErrorSet other)
    {
        var result = Empty;

        foreach (var errorCase in other.Cases)
        {
            var restricted = Restrict(errorCase.Type, true);

            if (errorCase.IsExact)
                restricted = new ErrorSet([.. restricted.Cases.Where(candidate =>
                    SymbolEqualityComparer.Default.Equals(candidate.Type, errorCase.Type)
                    && !candidate.Excluded.Any(excluded => IsSubtype(errorCase.Type, excluded)))
                    .Select(candidate => new ErrorCase(candidate.Type, true, []))]);

            foreach (var excluded in errorCase.Excluded)
                restricted = restricted.Restrict(excluded, false);

            result = result.Union(restricted);
        }

        return result;
    }

    // Exact types can be kept or removed directly. A family also represents descendants
    // that are not individually known: matching narrows its bound, while a failed test
    // excludes the tested family without discarding other descendants.
    internal ErrorSet Restrict(ITypeSymbol testedType, bool matches)
    {
        var errorCases = ImmutableArray.CreateBuilder<ErrorCase>();

        foreach (var errorCase in Cases)
        {
            // Failure to prove a relationship involving unbound type arguments
            // is not proof of disjointness. Preserve both paths until a concrete
            // call substitutes the types; exported summaries must keep them too.
            if (ContainsTypeParameters(errorCase.Type) || ContainsTypeParameters(testedType))
            {
                errorCases.Add(errorCase);

                continue;
            }

            if (errorCase.IsExact || IsSubtype(errorCase.Type, testedType))
            {
                if (IsSubtype(errorCase.Type, testedType) == matches)
                    errorCases.Add(errorCase);

                continue;
            }

            if (!IsSubtype(testedType, errorCase.Type))
            {
                if (!matches)
                    errorCases.Add(errorCase);

                continue;
            }

            if (matches)
            {
                if (!errorCase.Excluded.Any(excludedType => IsSubtype(testedType, excludedType)))
                    errorCases.Add(new ErrorCase(testedType, testedType.IsSealed, errorCase.Excluded));

                continue;
            }

            if (errorCase.Excluded.Any(excludedType => SymbolEqualityComparer.Default.Equals(testedType, excludedType)))
            {
                errorCases.Add(errorCase);
                continue;
            }

            errorCases.Add(new ErrorCase(errorCase.Type, false, errorCase.Excluded.Add(testedType)));
        }

        return new(errorCases.ToImmutable());
    }

    internal IEnumerable<string> DisplayNames() => Cases
        .Select(errorCase => errorCase.GetDisplayName())
        .Distinct()
        .OrderBy(errorName => errorName, System.StringComparer.Ordinal);

    // This relation proves class ancestry and effective generic constraints only.
    // Pattern coverage in the editor uses Roslyn conversions, including interfaces.
    // A false result for an unbound generic relationship does not prove disjointness.
    internal static bool IsSubtype(ITypeSymbol candidateType, ITypeSymbol expectedType)
    {
        if (candidateType is ITypeParameterSymbol parameter)
            return SymbolEqualityComparer.Default.Equals(candidateType, expectedType)
                   || parameter.ConstraintTypes.Any(constraint => IsSubtype(constraint, expectedType));

        for (var currentType = candidateType; currentType is not null; currentType = currentType.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(currentType, expectedType))
                return true;
        }

        return false;
    }

    internal static bool ContainsTypeParameters(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => true,
        IArrayTypeSymbol array => ContainsTypeParameters(array.ElementType),
        INamedTypeSymbol namedType => namedType.TypeArguments.Any(ContainsTypeParameters)
            || namedType.ContainingType is { } containingType && ContainsTypeParameters(containingType),
        _ => false
    };

    internal sealed class ErrorCase
    {
        internal ITypeSymbol Type { get; }
        internal bool IsExact { get; }
        internal ImmutableArray<ITypeSymbol> Excluded { get; }

        internal ErrorCase(ITypeSymbol type, bool exact, ImmutableArray<ITypeSymbol> excluded)
        {
            // Nullability belongs to FlowValue, not to the identity of an error type.
            Type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            IsExact = exact;
            Excluded = excluded;
        }

        internal bool SameAs(ErrorCase other) =>
            IsExact == other.IsExact
            && SymbolEqualityComparer.Default.Equals(Type, other.Type)
            && Excluded.Length == other.Excluded.Length
            && Excluded.All(excludedType => other.Excluded.Any(otherType =>
                SymbolEqualityComparer.Default.Equals(excludedType, otherType)));

        internal string GetDisplayName()
        {
            var displayName = Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

            if (!IsExact)
                displayName += " (+ subtypes)";

            if (Excluded.IsEmpty)
                return displayName;

            var excludedNames = Excluded
                .Select(excludedType => excludedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
                .OrderBy(errorName => errorName, System.StringComparer.Ordinal);

            return displayName + " except " + string.Join(", ", excludedNames);
        }
    }
}
