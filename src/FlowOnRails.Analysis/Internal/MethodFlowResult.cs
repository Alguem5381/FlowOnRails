using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.Analysis.Internal;

internal sealed class MethodFlowResult
{
    internal FlowValue ReturnValue { get; }
    internal bool UnresolvedConditions { get; }
    internal ImmutableArray<ErrorFlowLimitation> Limitations { get; }
    internal ImmutableArray<FlowValue> ParameterOutputs { get; }
    internal ImmutableArray<GuardedSummary> Variants { get; }

    internal MethodFlowResult(
        FlowValue abstractValue,
        bool hasUnresolvedCondition,
        ImmutableArray<ErrorFlowLimitation> limitations,
        ImmutableArray<FlowValue> parameterOutputs = default,
        ImmutableArray<GuardedSummary> variants = default)
    {
        ReturnValue = abstractValue;
        UnresolvedConditions = hasUnresolvedCondition;
        Limitations = [.. limitations.Distinct()
            .OrderBy(limitation => limitation.Message, StringComparer.Ordinal).ThenBy(limitation => limitation.Code)];
        ParameterOutputs = parameterOutputs.IsDefault ? [] : parameterOutputs;
        Variants = variants.IsDefault ? [] : variants;
    }
}
