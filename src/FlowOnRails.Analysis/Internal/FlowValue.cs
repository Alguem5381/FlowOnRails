using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analysis.Internal;

internal enum ValueKind
{
    Bottom,
    Other,
    Error,
    Rail,
    Boolean,
    Awaitable,
    Callable
}

internal enum ParameterProjection
{
    Identity,
    ErrorToFailure,
    RailToError,
    RailErrorsToFailure
}

internal sealed class FlowValue
{
    internal static readonly FlowValue Other = new(ValueKind.Other);
    internal static readonly FlowValue Bottom = new(ValueKind.Bottom);
    internal static readonly FlowValue Null = new(ValueKind.Error, mayBeNull: true);

    internal ValueKind Kind { get; }
    internal ErrorSet Errors { get; }
    internal bool MaySucceed { get; }
    internal bool MayBeInvalid { get; }
    internal bool MayBeNull { get; }
    internal bool? Boolean { get; }
    internal FlowCondition? Condition { get; }
    // Refinement permission, not just provenance: null must mean source success.
    // A transformation introducing null for other reasons must discard this link.
    internal Slot? RailSource { get; }
    internal FlowValue? AwaitedValue { get; }
    // A supported parameter projection can be substituted in exported summaries.
    // Its error envelope retains pattern restrictions; unmodeled transformations
    // and joins with unrelated origins drop the dependency.
    internal int? ParameterOrigin { get; }
    internal ImmutableArray<CallableTarget> Targets { get; }
    internal bool HasUnknownTarget { get; }
    internal ParameterProjection Projection { get; }
    internal bool IsBottom =>
        Kind == ValueKind.Bottom
        || Kind == ValueKind.Error && Errors.IsEmpty && !MayBeNull
        || Kind == ValueKind.Rail && Errors.IsEmpty && !MaySucceed && !MayBeInvalid;

    private FlowValue(
        ValueKind kind,
        ErrorSet? errors = null,
        bool maySucceed = false,
        bool mayBeInvalid = false,
        bool mayBeNull = false,
        bool? boolean = null,
        FlowCondition? condition = null,
        Slot? railSource = null,
        FlowValue? awaitedValue = null,
        int? parameterOrigin = null,
        ImmutableArray<CallableTarget> targets = default,
        bool hasUnknownTarget = false,
        ParameterProjection projection = ParameterProjection.Identity
    )
    {
        Kind = kind;
        Errors = errors ?? ErrorSet.Empty;
        MaySucceed = maySucceed;
        MayBeInvalid = mayBeInvalid;
        MayBeNull = mayBeNull;
        Boolean = boolean;
        Condition = condition;
        RailSource = railSource;
        AwaitedValue = awaitedValue;
        ParameterOrigin = parameterOrigin;
        Targets = targets.IsDefault ? [] : targets;
        HasUnknownTarget = hasUnknownTarget;
        Projection = projection;
    }

    internal static FlowValue
        Rail(ErrorSet errors, bool success, bool invalid = false) =>
        new(ValueKind.Rail, errors, maySucceed: success, mayBeInvalid: invalid);

    internal static FlowValue
        Error(ErrorSet errors, bool nullable = false, Slot? railSource = null) =>
        new(ValueKind.Error, errors, mayBeNull: nullable, railSource: railSource);

    internal static FlowValue Bool(bool? booleanValue, FlowCondition? condition = null) =>
        new(ValueKind.Boolean, boolean: booleanValue, condition: condition);

    internal static FlowValue Awaitable(FlowValue awaitedValue) =>
        new FlowValue(ValueKind.Awaitable, awaitedValue: awaitedValue);

    internal FlowValue UnwrapAwaitables() => AwaitedValue?.UnwrapAwaitables() ?? this;

    internal static FlowValue Callable(ImmutableArray<CallableTarget> targets, bool unknown = false) =>
        new FlowValue(ValueKind.Callable, targets: targets, hasUnknownTarget: unknown);

    internal FlowValue WithParameterOrigin(int parameterOrdinal) =>
        new FlowValue(Kind, Errors, MaySucceed, MayBeInvalid, MayBeNull, Boolean,
            Condition, RailSource, AwaitedValue, parameterOrdinal, Targets, HasUnknownTarget, Projection);

    internal FlowValue WithDependencyFrom(FlowValue source, ParameterProjection? projection = null) =>
        new FlowValue(Kind, Errors, MaySucceed, MayBeInvalid, MayBeNull, Boolean,
            Condition, RailSource, AwaitedValue, source.ParameterOrigin, Targets, HasUnknownTarget,
            projection ?? source.Projection);

    internal FlowValue WithoutLinks(Slot changedSlot) =>
        new FlowValue(
            Kind, Errors, MaySucceed, MayBeInvalid, MayBeNull, Boolean,
            Condition?.DependsOn(changedSlot) == true ? null : Condition,
            RailSource?.Equals(changedSlot) == true ? null : RailSource,
            AwaitedValue?.WithoutLinks(changedSlot), ParameterOrigin, Targets, HasUnknownTarget, Projection
        );

    // Conditions and source slots refer to a single method frame. Crossing a call
    // boundary preserves alternatives, but cannot retain links to another frame.
    internal FlowValue WithoutFlowLinks() =>
        new FlowValue(Kind, Errors, MaySucceed, MayBeInvalid, MayBeNull, Boolean,
            awaitedValue: AwaitedValue?.WithoutFlowLinks(), parameterOrigin: ParameterOrigin,
            targets: Targets, hasUnknownTarget: HasUnknownTarget, projection: Projection);

    internal FlowValue Join(FlowValue other)
    {
        if (Kind == ValueKind.Bottom)
            return other;

        if (other.Kind == ValueKind.Bottom)
            return this;

        if (Kind != other.Kind)
            return Other;

        return new FlowValue(
            Kind, Errors.Union(other.Errors), MaySucceed || other.MaySucceed,
            MayBeInvalid || other.MayBeInvalid, MayBeNull || other.MayBeNull,
            Boolean == other.Boolean ? Boolean : null,
            Condition?.SameAs(other.Condition) == true ? Condition : null,
            RailSource.Equals(other.RailSource) ? RailSource : null,
            AwaitedValue is { } awaitedValue && other.AwaitedValue is { } otherAwaited
                ? awaitedValue.Join(otherAwaited)
                : null,
            ParameterOrigin == other.ParameterOrigin && Projection == other.Projection ? ParameterOrigin : null,
            [.. Targets.Concat(other.Targets.Where(target => !Targets.Any(existing => existing.SameAs(target)))).Take(16)],
            HasUnknownTarget || other.HasUnknownTarget || Targets.Length + other.Targets.Length > 16,
            Projection == other.Projection ? Projection : ParameterProjection.Identity
        );
    }

    internal bool SameAs(FlowValue other) =>
        Kind == other.Kind
        && Errors.SameAs(other.Errors)
        && MaySucceed == other.MaySucceed
        && MayBeInvalid == other.MayBeInvalid
        && MayBeNull == other.MayBeNull
        && Boolean == other.Boolean
        && (Condition is null ? other.Condition is null : Condition.SameAs(other.Condition))
        && RailSource.Equals(other.RailSource)
        && ParameterOrigin == other.ParameterOrigin
        && Projection == other.Projection
        && HasUnknownTarget == other.HasUnknownTarget
        && Targets.Length == other.Targets.Length
        && Targets.All(target => other.Targets.Any(target.SameAs))
        && (AwaitedValue is null ? other.AwaitedValue is null :
            other.AwaitedValue is not null && AwaitedValue.SameAs(other.AwaitedValue));
}
