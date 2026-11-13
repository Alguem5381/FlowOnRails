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
    // Observe the governing expression before switch lowering tests individual
    // patterns. Repeated loop visits join possibilities. Called methods receive no
    // observer, so identical spans in another body cannot contaminate this value.
    private FlowValue EvaluateOperation(IOperation operation, FlowState state)
    {
        var mayThrow = _exceptionBlock is not null && MayThrow(operation);

        if (mayThrow)
            ObservePossibleException(state);

        var value = EvaluateOperationCore(operation, state);

        if (_analysisSession.HasMutableCallbacks && HasUnmodeledEffects(operation))
        {
            InvalidateTrackedValues(state);
            value = UnknownValue(operation.Type);
        }

        if (operation is IInvocationOperation && value.IsBottom)
            state.Reachable = false;

        // A call can change ref/out storage or captured state before throwing.
        // Preserve that possibility even when its normal continuation is dead.
        if (mayThrow)
            ObservePossibleException(state);

        if (_observation is { } observation
            && operation.Syntax.SyntaxTree == observation.Expression.SyntaxTree
            && operation.Syntax.Span == observation.Expression.Span
            && value.Kind == ValueKind.Error)
            observation.Value = observation.Value.Join(value);

        return value;
    }

    private FlowValue EvaluateOperationCore(IOperation operation, FlowState state)
    {
        _analysisSession.Cancellation.ThrowIfCancellationRequested();

        switch (operation)
        {
            case IExpressionStatementOperation statement:
                return EvaluateOperation(statement.Operation, state);

            case IVariableDeclarationGroupOperation group:
                foreach (var declaration in group.Declarations)
                    EvaluateOperation(declaration, state);

                return FlowValue.Other;

            case IVariableDeclarationOperation declaration:
                foreach (var declarator in declaration.Declarators)
                    EvaluateOperation(declarator, state);

                return FlowValue.Other;

            case IVariableDeclaratorOperation declarator:
                var initialValue = declarator.Initializer is { } initializer
                    ? EvaluateOperation(initializer.Value, state)
                    : UnknownValue(declarator.Symbol.Type);

                if (declarator.Symbol.RefKind != RefKind.None)
                {
                    var source = declarator.Initializer is { } referenceInitializer
                        ? GetSlot(referenceInitializer.Value) : null;
                    state.BindAlias(new Slot(declarator.Symbol), source);

                    if (source is null)
                        _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                            ErrorFlowLimitationCode.ReferenceAliasUntracked, "This reference alias target is not tracked"));

                    return initialValue;
                }

                state.Write(new Slot(declarator.Symbol), initialValue);

                return initialValue;

            case ISimpleAssignmentOperation assignment:
                EvaluateAssignmentTarget(assignment.Target, state);
                var assignedValue = EvaluateOperation(assignment.Value, state);

                if (GetSlot(assignment.Target) is { } target)
                {
                    if (assignment.IsRef)
                    {
                        var source = GetSlot(assignment.Value);
                        state.BindAlias(target, source);

                        if (source is null)
                            _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                                ErrorFlowLimitationCode.ReferenceAliasUntracked, "This reference alias target is not tracked"));

                        return assignedValue;
                    }

                    state.Write(target, assignedValue);
                }
                else
                {
                    if (_analysisSession.HasMutableCallbacks)
                        InvalidateTrackedValues(state);

                    if (_librarySymbols.IsRail(assignment.Target.Type)
                        || _librarySymbols.IsError(assignment.Target.Type))
                        _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                            ErrorFlowLimitationCode.StorageUntracked, "Storage outside local variables is not tracked"));
                }

                return assignedValue;

            case ICompoundAssignmentOperation assignment:
                EvaluateOperation(assignment.Target, state);
                EvaluateOperation(assignment.Value, state);

                return ForgetTarget(assignment.Target, state);

            case IIncrementOrDecrementOperation increment:
                EvaluateOperation(increment.Target, state);

                return ForgetTarget(increment.Target, state);

            case IDeconstructionAssignmentOperation assignment:
                EvaluateAssignmentTarget(assignment.Target, state);
                EvaluateOperation(assignment.Value, state);

                ForgetDeconstructionTarget(assignment.Target, state);

                _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                    ErrorFlowLimitationCode.DeconstructionUntracked, "Deconstruction assignments are not tracked precisely"));

                return FlowValue.Other;

            case IFlowCaptureOperation capture:
                var capturedValue = EvaluateOperation(capture.Value, state);
                var captureSlot = new Slot(capture.Id);

                state.Write(captureSlot, capturedValue);

                if (GetSlot(capture.Value) is { } capturedSlot)
                    state.LinkCapture(captureSlot, capturedSlot);

                return capturedValue;

            case ILocalReferenceOperation local:
                return ReadValue(new Slot(local.Local), local.Type, state);

            case IParameterReferenceOperation parameter:
                return ReadValue(new Slot(parameter.Parameter), parameter.Type, state);

            case IFlowCaptureReferenceOperation capture:
                return ReadValue(new Slot(capture.Id), capture.Type, state);

            case IDeclarationExpressionOperation declaration:
                return EvaluateOperation(declaration.Expression, state);

            case ILiteralOperation literal:
                return GetConstantValue(literal);

            case IAwaitOperation awaited:
                var awaitableValue = EvaluateOperation(awaited.Operation, state);
                var awaitedValue = awaitableValue.AwaitedValue ?? UnknownValue(awaited.Type);

                if (_analysisSession.HasMutableCallbacks)
                    InvalidateTrackedValues(state);

                if (awaitedValue.IsBottom)
                    state.Reachable = false;

                return awaitedValue;

            case ITypeParameterObjectCreationOperation creation:
                if (_analysisSession.HasMutableCallbacks)
                    InvalidateTrackedValues(state);

                if (creation.Initializer is { } typeParameterInitializer)
                    EvaluateOperation(typeParameterInitializer, state);

                return _librarySymbols.IsError(_types.Apply(creation.Type))
                    ? FlowValue.Error(ErrorSet.Of(_types.Apply(creation.Type)!, true))
                    : UnknownValue(creation.Type);

            case IDefaultValueOperation:
                if (operation.Type?.IsReferenceType == true && _librarySymbols.AwaitedType(operation.Type) is not null)
                    return FlowValue.Awaitable(FlowValue.Bottom);

                if (_librarySymbols.IsRail(operation.Type))
                    return FlowValue.Rail(ErrorSet.Empty, false, true);

                if (_librarySymbols.IsError(operation.Type))
                    return FlowValue.Null;

                return operation.Type?.SpecialType == SpecialType.System_Boolean
                    ? FlowValue.Bool(false)
                    : FlowValue.Other;

            case IObjectCreationOperation creation:
                FlowValue? firstArgument = null;

                foreach (var argument in creation.Arguments)
                {
                    var argumentValue = EvaluateOperation(argument.Value, state);

                    if (argument.Parameter?.Ordinal == 0)
                        firstArgument = argumentValue;

                    if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out
                        && GetSlot(argument.Value) is { } argumentSlot)
                        state.Write(argumentSlot, UnknownValue(argument.Value.Type));
                }

                if (MayInvokeCallbacks(creation.Arguments))
                    InvalidateTrackedValues(state);

                if (creation.Initializer is { } objectInitializer)
                    EvaluateOperation(objectInitializer, state);

                if (_librarySymbols.AwaitedType(_types.Apply(creation.Type)) is not null)
                {
                    if (creation.Arguments.Length != 1 || firstArgument is null)
                        return UnknownValue(creation.Type);

                    return firstArgument.Kind == ValueKind.Awaitable
                        ? firstArgument
                        : FlowValue.Awaitable(firstArgument);
                }

                if (_librarySymbols.IsError(creation.Type))
                    return FlowValue.Error(ErrorSet.Of(_types.Apply(creation.Type)!, true));

                return _librarySymbols.IsRail(creation.Type)
                    ? FlowValue.Rail(ErrorSet.Empty, false, true)
                    : UnknownValue(creation.Type);

            case IConversionOperation conversion:
                return EvaluateConversion(conversion, EvaluateOperation(conversion.Operand, state), state);

            case IInvocationOperation invocation:
                return EvaluateObservedInvocation(invocation, state);

            case IPropertyReferenceOperation property:
                return EvaluateProperty(property, state);

            case IIsPatternOperation test:
                var subjectValue = EvaluateOperation(test.Value, state);

                return EvaluateTest(new FlowCondition(
                    ConditionKind.Pattern, GetSlot(test.Value), subjectValue, test.Pattern
                ));

            case IIsTypeOperation test:
                var typeSubject = EvaluateOperation(test.ValueOperand, state);

                return EvaluateTest(new FlowCondition(
                    ConditionKind.Pattern, GetSlot(test.ValueOperand), typeSubject,
                    testedType: test.TypeOperand, negated: test.IsNegated
                ));

            case IIsNullOperation test:
                var nullSubject = EvaluateOperation(test.Operand, state);

                return EvaluateTest(new FlowCondition(ConditionKind.Pattern, GetSlot(test.Operand), nullSubject));

            case IUnaryOperation { OperatorKind: UnaryOperatorKind.Not } unaryOperation:
                var operand = EvaluateOperation(unaryOperation.Operand, state);

                return FlowValue.Bool(operand.Boolean is { } boolean ? !boolean : null, operand.Condition?.Not());

            case IBinaryOperation binaryOperation:
                var leftValue = EvaluateOperation(binaryOperation.LeftOperand, state);
                var rightValue = EvaluateOperation(binaryOperation.RightOperand, state);

                if (operation.ConstantValue is { HasValue: true, Value: bool constant })
                    return FlowValue.Bool(constant);

                if (binaryOperation.OperatorMethod is not null
                    || binaryOperation.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
                    || leftValue.Kind != ValueKind.Boolean
                    || rightValue.Kind != ValueKind.Boolean)
                    return UnknownValue(operation.Type);

                var knownBoolean = rightValue.Boolean ?? leftValue.Boolean;
                var testedValue = rightValue.Boolean.HasValue ? leftValue : rightValue;

                if (knownBoolean is not { } expectedBoolean)
                    return UnknownValue(operation.Type);

                var negateCondition = expectedBoolean == (binaryOperation.OperatorKind == BinaryOperatorKind.NotEquals);

                return FlowValue.Bool(
                    testedValue.Boolean is { } actualBoolean ? actualBoolean != negateCondition : null,
                    negateCondition ? testedValue.Condition?.Not() : testedValue.Condition
                );

            case IDelegateCreationOperation creation:
                return EvaluateOperation(creation.Target, state);

            case IFlowAnonymousFunctionOperation lambda:
                RegisterCallback(lambda);
                var lambdaGraph = _controlFlowGraph.GetAnonymousFunctionControlFlowGraph(lambda, _analysisSession.Cancellation);

                return FlowValue.Callable([new CallableTarget(lambda.Symbol, lambdaGraph, _types)]);

            case ILocalFunctionOperation:
                return FlowValue.Other; // Creating a callback does not execute its body.

            case IMethodReferenceOperation methodReference:
                if (methodReference.Instance is { } methodReceiver)
                    EvaluateOperation(methodReceiver, state);

                RegisterCallback(methodReference);
                var targetMethod = _types.Apply(methodReference.Method);

                return FlowValue.Callable([new CallableTarget(targetMethod, GetCalledGraph(targetMethod),
                    new TypeSubstitution(_librarySymbols.Compilation, targetMethod, _types), methodReference.IsVirtual)]);

            case IThrowOperation thrown:
                if (thrown.Exception is { } exception)
                    EvaluateOperation(exception, state);

                ObservePossibleException(state);
                state.Reachable = false;

                return FlowValue.Other;

            case IReturnOperation returned:
                return returned.ReturnedValue is { } resultValue
                    ? EvaluateOperation(resultValue, state)
                    : FlowValue.Other;

            default:
                foreach (var childOperation in operation.ChildOperations)
                    EvaluateOperation(childOperation, state);

                if (_librarySymbols.IsRail(operation.Type) || _librarySymbols.IsError(operation.Type))
                    _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                        ErrorFlowLimitationCode.UnsupportedOperation, "Unsupported value operation: " + operation.Kind));

                return operation.ConstantValue.HasValue
                    ? GetConstantValue(operation)
                    : UnknownValue(operation.Type);
        }
    }

    // Computing a write destination can call methods and mutate ref/captured
    // storage before the right-hand value is evaluated. Visit receivers and
    // indices once, without reading the property getter for a simple write.
    // Compound writes and increments do read the destination and use the normal
    // evaluator instead. Tuple destinations preserve left-to-right evaluation.
    private void EvaluateAssignmentTarget(IOperation target, FlowState state)
    {
        if (target is IInvocationOperation)
        {
            EvaluateOperation(target, state);

            return;
        }

        foreach (var child in target.ChildOperations)
            if (target is ITupleOperation or IDeclarationExpressionOperation
                or IConversionOperation { OperatorMethod: null })
                EvaluateAssignmentTarget(child, state);
            else
                EvaluateOperation(child, state);
    }

    private void ForgetDeconstructionTarget(IOperation target, FlowState state)
    {
        if (target is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements)
                ForgetDeconstructionTarget(element, state);

            return;
        }

        if (target is IDeclarationExpressionOperation declaration)
        {
            ForgetDeconstructionTarget(declaration.Expression, state);

            return;
        }

        // A local used to compute a receiver/index is not itself overwritten by
        // deconstruction. Forget only the storage locations being assigned.
        ForgetTarget(target, state);
    }

    private FlowValue EvaluateConversion(
        IConversionOperation operation,
        FlowValue abstractValue,
        FlowState state)
    {
        // A null Task cannot produce an awaited value. It is not a nullable
        // Error and must retain the awaitable shape across assembly summaries.
        if (operation.Type?.IsReferenceType == true
            && _librarySymbols.AwaitedType(operation.Type) is not null
            && abstractValue is { Kind: ValueKind.Error, Errors.IsEmpty: true, MayBeNull: true })
            return FlowValue.Awaitable(FlowValue.Bottom);

        if (operation.OperatorMethod is { } methodSymbol)
        {
            if (_librarySymbols.IsRail(methodSymbol.ContainingType) && _librarySymbols.IsRail(operation.Type))
            {
                // The selected operator determines the outer state. A rail passed
                // as TValue is a success payload, even if that inner rail failed.
                var sourceParameterType = methodSymbol.OriginalDefinition.Parameters[0].Type;

                if (sourceParameterType is ITypeParameterSymbol)
                    return CreateSuccess(abstractValue, state);

                if (_librarySymbols.IsError(sourceParameterType))
                    return CreateFailure(abstractValue, state);

                // RailFor<TValue> -> Rail preserves failure/success alternatives.
                if (_librarySymbols.IsRail(sourceParameterType)
                    && methodSymbol.ReturnType is INamedTypeSymbol { Arity: 0 })
                    return abstractValue.Kind == ValueKind.Rail ? abstractValue : UnknownValue(operation.Type);
            }

            _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                ErrorFlowLimitationCode.UnsupportedConversion, "User-defined conversions are not analyzed between methods"));

            return UnknownValue(operation.Type);
        }

        if (abstractValue.Kind != ValueKind.Error
            || operation.Conversion.IsIdentity
            || operation.IsImplicit)
            return abstractValue;

        if (!_librarySymbols.IsError(operation.Type))
        {
            if (!operation.IsTryCast)
                return abstractValue;

            _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                ErrorFlowLimitationCode.UnsupportedConversion, "Try-casts outside the error hierarchy are not tracked precisely"));

            return UnknownValue(operation.Type);
        }

        // A failed 'as' can introduce null even when the source rail has an error.
        // Such a value must not refine that rail as if null implied success.
        var castMayProduceNull = operation.IsTryCast
                                 && !abstractValue.Errors.Restrict(_types.Apply(operation.Type)!, false).IsEmpty;
        var filteredValue = FlowValue.Error(
            abstractValue.Errors.Restrict(_types.Apply(operation.Type)!, true),
            abstractValue.MayBeNull || castMayProduceNull,
            castMayProduceNull ? null : abstractValue.RailSource
        ).WithDependencyFrom(abstractValue);

        if (filteredValue.IsBottom)
            state.Reachable = false;

        return filteredValue;
    }
}
