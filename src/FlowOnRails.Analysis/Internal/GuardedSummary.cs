using System.Collections.Immutable;

namespace FlowOnRails.Analysis.Internal;

internal sealed class GuardedSummary
{
    internal ImmutableArray<FlowValue> Inputs { get; }
    internal MethodFlowResult Result { get; }

    internal GuardedSummary(ImmutableArray<FlowValue> inputs, MethodFlowResult result) =>
        (Inputs, Result) = (inputs, result);

    internal static FlowValue Intersect(FlowValue argument, FlowValue guard)
    {
        if (argument.Kind != guard.Kind)
            return argument;

        return guard.Kind switch
        {
            ValueKind.Boolean => argument.Boolean is { } actual && guard.Boolean is { } expected && actual != expected
                ? FlowValue.Bottom : FlowValue.Bool(argument.Boolean ?? guard.Boolean),
            ValueKind.Error => FlowValue.Error(argument.Errors.Intersect(guard.Errors), argument.MayBeNull && guard.MayBeNull),
            ValueKind.Rail => FlowValue.Rail(argument.Errors.Intersect(guard.Errors),
                argument.MaySucceed && guard.MaySucceed, argument.MayBeInvalid && guard.MayBeInvalid),
            _ => argument
        };
    }
}
