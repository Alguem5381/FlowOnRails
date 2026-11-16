using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.Analysis.Internal;

internal sealed partial class MethodFlowAnalysis
{
    private static bool? GetKnownSuccess(FlowValue railValue)
    {
        if (railValue.Errors.IsEmpty)
            return true;

        return railValue.MaySucceed ? null : false;
    }

    private FlowValue EvaluateTest(FlowCondition condition)
    {
        var matchingValue = FilterValue(condition.Value, condition, true, out _);
        var nonMatchingValue = FilterValue(condition.Value, condition, false, out _);
        bool? knownResult = null;

        if (matchingValue.IsBottom)
            knownResult = false;
        else if (nonMatchingValue.IsBottom)
            knownResult = true;

        return FlowValue.Bool(
            condition.Negated && knownResult.HasValue
                ? !knownResult.Value
                : knownResult,
            condition
        );
    }

    // A condition constrains each path independently. Unknown conditions keep both
    // paths and affect precision, not the set of possible error types by themselves.
    // Extraction also links its error back to the rail so type tests refine that source.
    private FlowState ConstrainBranch(FlowState state, FlowValue conditionValue, bool truth)
    {
        if (conditionValue.Boolean is { } constant && constant != truth)
        {
            state.Reachable = false;

            return state;
        }

        if (conditionValue.Condition is not { } condition)
        {
            if (!conditionValue.Boolean.HasValue)
                _analysisSession.UnresolvedConditions = true;

            return state;
        }

        truth = truth != condition.Negated;
        var subjectValue = condition.Subject is { } slot && state.TryGet(slot, out var currentValue)
            ? currentValue
            : condition.Value;

        if (condition.Kind == ConditionKind.RailSuccess)
        {
            var narrowedValue = FlowValue.Rail(
                truth ? ErrorSet.Empty : subjectValue.Errors,
                truth && subjectValue.MaySucceed
            ).WithDependencyFrom(subjectValue);
            state.Reachable = !narrowedValue.IsBottom;

            if (condition.Subject is { } sourceSlot)
                state.Refine(sourceSlot, narrowedValue);

            if (condition.OutError is { } errorSlot)
                state.Refine(
                    errorSlot,
                    (truth ? FlowValue.Null : FlowValue.Error(subjectValue.Errors, railSource: condition.Subject))
                        .WithDependencyFrom(subjectValue, ParameterProjection.RailToError)
                );

            return state;
        }

        var filteredValue = FilterValue(subjectValue, condition, truth, out var hasUnresolvedCondition);
        _analysisSession.UnresolvedConditions |= hasUnresolvedCondition;
        state.Reachable = !filteredValue.IsBottom;

        if (condition.Subject is { } target)
            state.Refine(target, filteredValue);

        if (filteredValue.RailSource is { } railSlot && state.TryGet(railSlot, out var railValue))
            state.Refine(railSlot,
                FlowValue.Rail(filteredValue.Errors, railValue.MaySucceed && filteredValue.MayBeNull)
                    .WithDependencyFrom(railValue));

        if (truth && condition.Pattern is { } pattern)
            BindPattern(pattern, filteredValue, state);

        return state;
    }

    private FlowValue FilterValue(
        FlowValue abstractValue,
        FlowCondition condition,
        bool truth,
        out bool hasUnresolvedCondition)
    {
        if (condition.TestedType is { } type)
            return FilterType(abstractValue, type, truth, out hasUnresolvedCondition);

        if (condition.Pattern is { } pattern)
            return FilterPattern(abstractValue, pattern, truth, out hasUnresolvedCondition);

        hasUnresolvedCondition = abstractValue.Kind != ValueKind.Error;

        if (hasUnresolvedCondition)
            return abstractValue;

        return FlowValue.Error(
            truth ? ErrorSet.Empty : abstractValue.Errors,
            truth && abstractValue.MayBeNull,
            abstractValue.RailSource
        ).WithDependencyFrom(abstractValue);
    }

    private FlowValue FilterPattern(
        FlowValue abstractValue,
        IPatternOperation pattern,
        bool truth,
        out bool hasUnresolvedCondition)
    {
        hasUnresolvedCondition = false;

        switch (pattern)
        {
            case IDiscardPatternOperation:
            case IDeclarationPatternOperation { MatchesNull: true }:
                return truth ? abstractValue : FlowValue.Error(ErrorSet.Empty);

            case IDeclarationPatternOperation declaration:
                return FilterType(abstractValue, declaration.MatchedType!, truth, out hasUnresolvedCondition);

            case ITypePatternOperation type:
                return FilterType(abstractValue, type.MatchedType, truth, out hasUnresolvedCondition);

            case INegatedPatternOperation negated:
                return FilterPattern(abstractValue, negated.Pattern, !truth, out hasUnresolvedCondition);

            case IBinaryPatternOperation binaryOperation:
                // An AND on the matching path intersects possibilities. Its false path
                // is a union; OR reverses those rules, preserving partial matches.
                var intersection = (binaryOperation.OperatorKind == BinaryOperatorKind.And) == truth;
                var leftValue = FilterPattern(abstractValue, binaryOperation.LeftPattern, truth,
                    out var leftUnresolved);
                var rightValue = FilterPattern(
                    intersection ? leftValue : abstractValue,
                    binaryOperation.RightPattern,
                    truth,
                    out var rightUnresolved
                );
                hasUnresolvedCondition = leftUnresolved || rightUnresolved;

                return intersection ? rightValue : leftValue.Join(rightValue);

            case IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: null } }:
                hasUnresolvedCondition = abstractValue.Kind != ValueKind.Error;

                if (hasUnresolvedCondition)
                    return abstractValue;

                return FlowValue.Error(
                    truth ? ErrorSet.Empty : abstractValue.Errors,
                    truth && abstractValue.MayBeNull,
                    abstractValue.RailSource
                ).WithDependencyFrom(abstractValue);

            default:
                // Property/relational patterns must not exclude a whole error type.
                hasUnresolvedCondition = true;

                return abstractValue;
        }
    }

    private FlowValue FilterType(
        FlowValue abstractValue,
        ITypeSymbol testedType,
        bool truth,
        out bool hasUnresolvedCondition)
    {
        testedType = _types.Apply(testedType)!;
        hasUnresolvedCondition = abstractValue.Kind != ValueKind.Error || !_librarySymbols.IsError(testedType);

        if (hasUnresolvedCondition)
            return abstractValue;

        hasUnresolvedCondition = ErrorSet.ContainsTypeParameters(testedType)
            || abstractValue.Errors.Cases.Any(errorCase => ErrorSet.ContainsTypeParameters(errorCase.Type));

        return FlowValue.Error(
            abstractValue.Errors.Restrict(testedType, truth),
            !truth && abstractValue.MayBeNull,
            abstractValue.RailSource
        ).WithDependencyFrom(abstractValue);
    }

    private static void BindPattern(
        IPatternOperation pattern,
        FlowValue abstractValue,
        FlowState state)
    {
        if (pattern is IDeclarationPatternOperation { DeclaredSymbol: { } symbol })
            state.Refine(new Slot(symbol), abstractValue);
    }
}
