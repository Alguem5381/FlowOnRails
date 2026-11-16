using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analysis;

/// <summary>Describes an exact error type or a family of possible error types.</summary>
public sealed class ErrorFlowCase
{
    /// <summary>Gets the exact type, or the upper bound of an open family.</summary>
    public ITypeSymbol Type { get; }

    /// <summary>Gets whether this case represents only the named runtime type.</summary>
    public bool IsExact { get; }

    /// <summary>Gets type families excluded from this case by supported flow tests.</summary>
    public ImmutableArray<ITypeSymbol> ExcludedTypes { get; }

    internal ErrorFlowCase(ITypeSymbol type, bool isExact, ImmutableArray<ITypeSymbol> excludedTypes) =>
        (Type, IsExact, ExcludedTypes) = (type, isExact, excludedTypes);
}
