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
    private FlowValue EvaluateRailOperation(
        IInvocationOperation invocation,
        FlowValue railValue,
        FlowValue[] arguments,
        FlowState state,
        string? modeledOperator = null,
        bool awaitCallbacks = false)
    {
        var operatorName = modeledOperator ?? invocation.TargetMethod.Name;
        var failure = FlowValue.Rail(railValue.Errors, false);

        if (railValue is { MaySucceed: false, Errors.IsEmpty: true })
        {
            // Observing an uninitialized rail throws instead of returning a domain error.
            state.Reachable = false;

            return failure;
        }

        if (operatorName == "TryUnwrap")
        {
            // In both rail contracts the final parameter is the failure output.
            // Its role must not be inferred from its type: TValue may itself be Error.
            var errorParameter = invocation.TargetMethod.OriginalDefinition.Parameters.Last();
            var errorArgument = invocation.Arguments.First(argument =>
                argument.Parameter?.Ordinal == errorParameter.Ordinal);
            var errorSlot = GetSlot(errorArgument.Value);
            var sourceSlot = GetSlot(invocation.Instance!);

            foreach (var argument in invocation.Arguments)
            {
                if (GetSlot(argument.Value) is { } slot)
                    state.Write(
                        slot,
                        argument.Parameter?.Ordinal == errorParameter.Ordinal
                            ? FlowValue.Error(railValue.Errors, railValue.MaySucceed, sourceSlot)
                                .WithDependencyFrom(railValue, ParameterProjection.RailToError)
                            : UnknownValue(argument.Parameter?.Type)
                    );
            }

            return FlowValue.Bool(
                GetKnownSuccess(railValue),
                new FlowCondition(ConditionKind.RailSuccess, sourceSlot, railValue, outError: errorSlot)
            );
        }

        // Success-path operators preserve existing failures. Only their applicable
        // callbacks can introduce another returned value or error.
        if (operatorName is "Map" or "Bind" or "Check" or "Ensure" or "Tap" or "TapIf")
        {
            if (!railValue.MaySucceed)
                return failure;

            if (operatorName == "Ensure")
            {
                var predicate = EvaluateCallback(0, null);
                var output = failure;

                if (predicate is null)
                    return output;

                if (predicate.Boolean != false)
                    output = output.Join(FlowValue.Rail(ErrorSet.Empty, true));

                if (predicate.Boolean != true)
                {
                    var error = invocation.TargetMethod.Parameters[1].Type.TypeKind == TypeKind.Delegate
                        ? EvaluateCallback(1, null)
                        : arguments[1];

                    if (error is not null)
                        output = output.Join(CreateFailure(error, state, terminate: false));

                    if (predicate.Boolean is null)
                        _analysisSession.UnresolvedConditions = true;
                }

                return output;
            }

            if (operatorName == "TapIf")
            {
                var predicate = invocation.TargetMethod.Parameters[0].Type.TypeKind == TypeKind.Delegate
                    ? EvaluateCallback(0, null)
                    : arguments[0];

                if (predicate is null)
                    return failure;

                if (predicate.Boolean == false)
                    return FlowValue.Rail(railValue.Errors, true);

                var observerResult = EvaluateCallback(1, null);

                if (predicate.Boolean is null)
                    _analysisSession.UnresolvedConditions = true;

                return failure.Join(FlowValue.Rail(
                    ErrorSet.Empty, observerResult is not null || predicate.Boolean is null
                ));
            }

            var callbackResult = EvaluateCallback(0, null);

            if (callbackResult is null)
                return failure;

            if (operatorName is "Tap" or "Map")
                return failure.Join(
                    operatorName == "Map"
                        ? CreateSuccess(callbackResult, state, terminate: false)
                        : FlowValue.Rail(ErrorSet.Empty, true)
                );

            if (callbackResult.Kind != ValueKind.Rail)
                callbackResult = UnknownValue(invocation.Type);

            return failure.Join(FlowValue.Rail(callbackResult.Errors, callbackResult.MaySucceed));
        }

        // Failure-path callbacks can replace or recover an error. Merely observing
        // it leaves the original failure in the returned set.
        if (operatorName is "MapError" or "Recover" or "RecoverWith" or "TapError")
        {
            if (railValue.Errors.IsEmpty)
                return FlowValue.Rail(ErrorSet.Empty, railValue.MaySucceed);

            var callbackResult = EvaluateCallback(0, FlowValue.Error(railValue.Errors));
            var successfulRail = FlowValue.Rail(ErrorSet.Empty, railValue.MaySucceed);

            if (callbackResult is null)
                return successfulRail;

            return operatorName switch
            {
                "MapError" => successfulRail.Join(CreateFailure(callbackResult, state, terminate: false)),
                "Recover" => successfulRail.Join(CreateSuccess(callbackResult, state, terminate: false)),
                "RecoverWith" => successfulRail.Join(
                    callbackResult.Kind == ValueKind.Rail
                        ? FlowValue.Rail(callbackResult.Errors, callbackResult.MaySucceed)
                        : UnknownValue(invocation.Type)
                ),
                _ => successfulRail.Join(failure)
            };
        }

        if (operatorName == "Match")
        {
            var successfulResult = railValue.MaySucceed
                ? EvaluateCallback(0, null)
                : null;
            var failedResult = !railValue.Errors.IsEmpty
                ? EvaluateCallback(1, FlowValue.Error(railValue.Errors))
                : null;

            if (successfulResult is null)
                return failedResult ?? UnknownValue(invocation.Type);

            return failedResult is null ? successfulResult : successfulResult.Join(failedResult);
        }

        _analysisSession.Limitations.Add(new ErrorFlowLimitation(
            ErrorFlowLimitationCode.UnsupportedOperation, "Unsupported rail operator: " + operatorName));

        return UnknownValue(invocation.Type);

        FlowValue? EvaluateCallback(int parameterOrdinal, FlowValue? argumentValue)
        {
            var callbackType = (INamedTypeSymbol)_types.Apply(invocation.TargetMethod.Parameters[parameterOrdinal].Type)!;
            var callbackArguments = callbackType.DelegateInvokeMethod!.Parameters
                .Select(parameter => argumentValue ?? UnknownValue(parameter.Type)).ToArray();
            var beforeCallback = state.Copy();
            var callbackValue = InvokeCallable(arguments[parameterOrdinal], callbackArguments, [], state,
                callbackType.DelegateInvokeMethod.ReturnType);

            // The receiver can bypass this callback. Retain that state, including
            // for throwing callbacks, rather than making their writes unconditional.
            var mayBypass = operatorName == "Match"
                ? railValue.MaySucceed && !railValue.Errors.IsEmpty
                : operatorName is "MapError" or "Recover" or "RecoverWith" or "TapError"
                    ? railValue.MaySucceed
                    : !railValue.Errors.IsEmpty || operatorName is "TapIf" or "Ensure";

            if (mayBypass)
            {
                state.Reachable = true;
                state.Merge(beforeCallback);
            }

            if (awaitCallbacks)
                callbackValue = callbackValue?.UnwrapAwaitables();

            return callbackValue?.IsBottom == true ? null : callbackValue;
        }
    }
}
