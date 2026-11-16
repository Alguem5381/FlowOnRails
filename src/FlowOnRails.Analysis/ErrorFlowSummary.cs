using System.Collections.Immutable;
using System.Linq;

using FlowOnRails.Analysis.Internal;
using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analysis;

/// <summary>A conservative error-flow summary after unwrapping supported awaitables.</summary>
public sealed class ErrorFlowSummary
{
    /// <summary>Gets all error alternatives, including open families and exclusions.</summary>
    public ImmutableArray<ErrorFlowCase> ErrorCases { get; }

    /// <summary>Gets named error types or family bounds, excluding the base Error marker.</summary>
    public ImmutableArray<ITypeSymbol> KnownErrorTypes { get; }

    /// <summary>Gets whether an error case can include unnamed subtypes.</summary>
    public bool HasUnknownErrors { get; }

    /// <summary>Gets whether supported flow can produce a result after awaiting it.</summary>
    public bool MayProduceResult { get; }

    /// <summary>Gets whether a successful rail may be produced.</summary>
    public bool MaySucceed { get; }

    /// <summary>Gets whether an uninitialized rail may be produced.</summary>
    public bool MayBeInvalid { get; }

    /// <summary>Gets whether an Error-returning method may produce null.</summary>
    public bool MayReturnNull { get; }

    /// <summary>Gets whether undetermined conditions may broaden the error list.</summary>
    public bool HasUnresolvedConditions { get; }

    /// <summary>Gets human-readable analysis limits; these strings are not stable identifiers.</summary>
    /// <remarks>Use <see cref="LimitationDetails"/> for stable programmatic classification.</remarks>
    public ImmutableArray<string> Limitations { get; }

    /// <summary>Gets classified limits for programmatic use, including propagated limits from nested calls.</summary>
    public ImmutableArray<ErrorFlowLimitation> LimitationDetails { get; }

    /// <summary>Gets how the root method query was resolved; nested calls may have different origins.</summary>
    public ErrorFlowSummaryOrigin Origin { get; }

    internal ErrorFlowSummary(MethodFlowResult result, LibrarySymbols symbols, ErrorFlowSummaryOrigin origin)
    {
        var value = result.ReturnValue.UnwrapAwaitables();
        ErrorCases = [.. value.Errors.Cases.Select(errorCase =>
            new ErrorFlowCase(errorCase.Type, errorCase.IsExact, errorCase.Excluded))];
        KnownErrorTypes = [.. FlowQueries.KnownTypes(value.Errors, symbols)];
        HasUnknownErrors = value.Errors.IsOpen;
        MayProduceResult = !value.IsBottom;
        MaySucceed = value.MaySucceed;
        MayBeInvalid = value.MayBeInvalid;
        MayReturnNull = value.MayBeNull;
        HasUnresolvedConditions = result.UnresolvedConditions;
        LimitationDetails = result.Limitations;
        Limitations = [.. result.Limitations.Select(limitation => limitation.Message).Distinct()];
        Origin = origin;
    }
}
