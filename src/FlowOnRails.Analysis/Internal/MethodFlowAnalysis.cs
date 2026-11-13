using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.Analysis.Internal;

/// <summary>Interprets operations and bounded calls to concrete methods in the current compilation.</summary>
internal sealed partial class MethodFlowAnalysis
{
    private const int MaximumBlockVisits = 4096;
    private const int MaximumCallDepth = 32;
    private readonly LibrarySymbols _librarySymbols;
    private readonly TypeSubstitution _types;
    private readonly MethodBodyResolver _methodBodies;
    private readonly ControlFlowGraph _controlFlowGraph;
    private readonly IMethodSymbol _methodSymbol;
    private readonly AnalysisSession _analysisSession;
    private readonly IDictionary<IInvocationOperation, MethodFlowResult>? _invocationResults;
    private readonly FlowObservation? _observation;

    private MethodFlowAnalysis(
        LibrarySymbols librarySymbols,
        MethodBodyResolver methodBodies,
        ControlFlowGraph controlFlowGraph,
        IMethodSymbol methodSymbol,
        AnalysisSession analysisSession,
        TypeSubstitution? types = null,
        IDictionary<IInvocationOperation, MethodFlowResult>? invocationResults = null,
        FlowObservation? observation = null)
    {
        _librarySymbols = librarySymbols;
        _types = types ?? new TypeSubstitution(librarySymbols.Compilation, methodSymbol);
        _methodBodies = methodBodies;
        _controlFlowGraph = controlFlowGraph;
        _methodSymbol = methodSymbol;
        _analysisSession = analysisSession;
        _invocationResults = invocationResults;
        _observation = observation;
    }

    internal static MethodFlowResult Analyze(
        LibrarySymbols librarySymbols,
        MethodBodyResolver methodBodies,
        ControlFlowGraph controlFlowGraph,
        IMethodSymbol methodSymbol,
        CancellationToken cancellationToken,
        IDictionary<IInvocationOperation, MethodFlowResult>? invocationResults = null,
        FlowObservation? observation = null,
        FlowValue[]? initialArguments = null)
    {
        var analysisSession = new AnalysisSession(cancellationToken);
        var entryState = new FlowState();

        foreach (var parameter in methodSymbol.Parameters)
            entryState.Write(new Slot(parameter),
                (initialArguments is not null && parameter.Ordinal < initialArguments.Length
                    ? initialArguments[parameter.Ordinal] : librarySymbols.Unknown(parameter.Type))
                .WithParameterOrigin(parameter.Ordinal));

        var rootArguments = methodSymbol.Parameters.Select(parameter =>
        {
            entryState.TryGet(new Slot(parameter), out var value);

            return value;
        }).ToArray();
        var rootApproximation = analysisSession.GetApproximation(methodSymbol, rootArguments);
        FlowValue? analysisResult = null;
        MethodFlowAnalysis? methodAnalysis = null;

        // Recursive calls use the previous approximation for the same abstract
        // inputs. Start at bottom (no normal return), grow monotonically, and publish
        // a closed result only after every participating context stops changing.
        for (var iteration = 0; iteration < 16; iteration++)
        {
            analysisSession.ApproximationChanged = false;
            rootApproximation.Active = true;
            methodAnalysis = new MethodFlowAnalysis(
                librarySymbols, methodBodies, controlFlowGraph, methodSymbol, analysisSession,
                invocationResults: invocationResults, observation: observation);
            analysisResult = methodAnalysis.AnalyzeMethodReturn(entryState.Copy()) ?? FlowValue.Bottom;
            rootApproximation.Active = false;
            analysisSession.UpdateApproximation(rootApproximation, analysisResult);

            if (!analysisSession.UsedRecursion || !analysisSession.ApproximationChanged)
                break;

            if (iteration == 15 || analysisSession.BlockVisits >= MaximumBlockVisits)
            {
                analysisSession.Limitations.Add(new ErrorFlowLimitation(
                    ErrorFlowLimitationCode.RecursionDidNotConverge, "Recursive analysis did not converge within the analysis budget"));
                analysisResult = analysisResult.Join(librarySymbols.Unknown(methodSymbol.ReturnType));
                break;
            }
        }

        if (librarySymbols.IsTrackedReturn(methodSymbol.ReturnType)
            && analysisResult is not null
            && !librarySymbols.MatchesReturnType(methodSymbol.ReturnType, analysisResult))
        {
            analysisSession.Limitations.Add(new ErrorFlowLimitation(
                ErrorFlowLimitationCode.ReturnValueUnrepresented, "The returned value could not be represented"));
            analysisResult = librarySymbols.Unknown(methodSymbol.ReturnType);
        }

        return new MethodFlowResult(
            analysisResult ?? FlowValue.Bottom,
            analysisSession.UnresolvedConditions,
            [.. analysisSession.Limitations.OrderBy(limitation => limitation.Message, StringComparer.Ordinal)],
            [.. methodSymbol.Parameters.Select(parameter =>
            {
                var output = methodAnalysis?._normalExitState is { } exitState
                    && exitState.TryGet(new Slot(parameter), out var value)
                        ? value : librarySymbols.Unknown(parameter.Type);

                return librarySymbols.MatchesReturnType(parameter.Type, output)
                    ? output.WithoutFlowLinks() : librarySymbols.Unknown(parameter.Type);
            })]
        );
    }

    private FlowValue? AnalyzeMethodReturn(FlowState entryState)
    {
        var returnedValue = AnalyzeControlFlow(entryState);

        return _methodSymbol.IsAsync
            ? FlowValue.Awaitable(returnedValue ?? FlowValue.Bottom)
            : returnedValue;
    }

    private FlowValue UnknownValue(ITypeSymbol? type) => _librarySymbols.Unknown(_types.Apply(type));

    private ITypeSymbol BodyReturnType => _methodSymbol.IsAsync
        ? _librarySymbols.AwaitedType(_types.Apply(_methodSymbol.ReturnType)) ?? _types.Apply(_methodSymbol.ReturnType)!
        : _types.Apply(_methodSymbol.ReturnType)!;

    // Observe calls in their caller's CFG, before later recovery can change the
    // caller's return. Loop visits join alternatives instead of publishing repeats.
    // Nested method frames do not inherit this sink: their source is analyzed by
    // their own operation-block action, independently of this caller's arguments.
    private FlowValue EvaluateObservedInvocation(IInvocationOperation invocation, FlowState state)
    {
        if (_invocationResults is null
            || invocation.IsImplicit
            || invocation.Type is not { } invocationType
            || !(_librarySymbols.IsRail(invocationType) || _librarySymbols.IsAwaitableRail(invocationType)))
            return EvaluateInvocation(invocation, state);

        // Isolate each call's explanations. Limitations from an earlier unrelated
        // call still affect the method summary, but must not leak into this Info.
        var previousLimitations = _analysisSession.Limitations;
        var previousUnresolvedConditions = _analysisSession.UnresolvedConditions;
        _analysisSession.Limitations = [];
        _analysisSession.UnresolvedConditions = false;

        try
        {
            var returnedValue = EvaluateInvocation(invocation, state);
            var observedValue = returnedValue;

            if (observedValue.UnwrapAwaitables().Kind is not (ValueKind.Rail or ValueKind.Bottom))
            {
                _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                    ErrorFlowLimitationCode.ReturnValueUnrepresented, "The called method's returned value could not be represented"));
                observedValue = UnknownValue(invocation.Type);
            }

            var result = new MethodFlowResult(
                observedValue,
                _analysisSession.UnresolvedConditions,
                [.. _analysisSession.Limitations.OrderBy(limitation => limitation.Message, StringComparer.Ordinal)]
            );

            if (_invocationResults.TryGetValue(invocation, out var previousResult))
                result = new MethodFlowResult(
                    previousResult.ReturnValue.Join(observedValue),
                    previousResult.UnresolvedConditions || result.UnresolvedConditions,
                    [.. previousResult.Limitations.Union(result.Limitations).OrderBy(limitation => limitation.Message, StringComparer.Ordinal)]
                );

            _invocationResults[invocation] = result;

            return returnedValue;
        }
        finally
        {
            previousLimitations.UnionWith(_analysisSession.Limitations);
            _analysisSession.Limitations = previousLimitations;
            _analysisSession.UnresolvedConditions |= previousUnresolvedConditions;
        }
    }

    private FlowValue EvaluateInvocation(IInvocationOperation invocation, FlowState state)
    {
        var receiver = invocation.Instance is { } instance ? EvaluateOperation(instance, state) : FlowValue.Other;
        var arguments = new FlowValue[invocation.TargetMethod.Parameters.Length];
        var referenceArguments = new Dictionary<int, ImmutableArray<Slot>>();

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is { RefKind: RefKind.Ref or RefKind.Out or RefKind.In } referenceParameter)
                referenceArguments[referenceParameter.Ordinal] = GetSlot(argument.Value) is { } referenceSlot
                    ? state.ReferenceStorage(referenceSlot) : [];

            var argumentValue = argument.Parameter?.RefKind == RefKind.Out
                ? UnknownValue(argument.Value.Type)
                : EvaluateOperation(argument.Value, state);

            if (argument.Parameter is { } parameter && parameter.Ordinal < arguments.Length)
                arguments[parameter.Ordinal] = argumentValue;
        }

        if (_librarySymbols.IsTaskContainer(invocation.TargetMethod.ContainingType))
        {
            if (invocation.TargetMethod.Name == "FromResult" && arguments.Length == 1)
                return FlowValue.Awaitable(arguments[0]);

            if (invocation.TargetMethod.Name is "FromException" or "FromCanceled")
                return FlowValue.Awaitable(FlowValue.Bottom);

            if (invocation.TargetMethod.Name is "ConfigureAwait" or "AsTask"
                && receiver.Kind == ValueKind.Awaitable)
                return receiver;
        }

        if (_librarySymbols.IsAsyncOperator(invocation.TargetMethod)
            && receiver.AwaitedValue is { Kind: ValueKind.Rail } sourceRail)
        {
            var operatorName = invocation.TargetMethod.Name == "BindValueTaskAsync"
                ? "Bind"
                : invocation.TargetMethod.Name.Substring(0, invocation.TargetMethod.Name.Length - "Async".Length);
            var result = EvaluateRailOperation(invocation, sourceRail, arguments, state, operatorName, true);

            return FlowValue.Awaitable(result);
        }

        if (_librarySymbols.IsRail(invocation.TargetMethod.ContainingType))
        {
            if (invocation.TargetMethod.IsStatic)
            {
                if (invocation.TargetMethod.Name == "Ok")
                    return invocation.Arguments.IsEmpty
                        ? FlowValue.Rail(ErrorSet.Empty, true)
                        : CreateSuccess(arguments[0], state);

                if (invocation.TargetMethod.Name == "Fail")
                    return CreateFailure(arguments[0], state);
            }
            else if (receiver.Kind == ValueKind.Rail)
                return EvaluateRailOperation(invocation, receiver, arguments, state);
        }

        if (invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke)
            return InvokeCallable(receiver, arguments, referenceArguments, state, invocation.Type);

        return InvokeMethod(_types.Apply(invocation.TargetMethod), arguments, referenceArguments,
            state, invocation.IsVirtual);
    }

    private FlowValue EvaluateProperty(IPropertyReferenceOperation property, FlowState state)
    {
        var receiverValue = property.Instance is { } instance ? EvaluateOperation(instance, state) : FlowValue.Other;

        if (_librarySymbols.IsRail(property.Property.ContainingType)
            && receiverValue.Kind == ValueKind.Rail
            && property.Property.Name is "IsSuccess" or "IsFailure")
        {
            if (receiverValue is { MaySucceed: false, Errors.IsEmpty: true })
                state.Reachable = false;

            var condition = new FlowCondition(
                ConditionKind.RailSuccess, GetSlot(property.Instance!), receiverValue,
                negated: property.Property.Name == "IsFailure"
            );
            var knownSuccess = GetKnownSuccess(receiverValue);

            return FlowValue.Bool(
                condition.Negated && knownSuccess.HasValue ? !knownSuccess.Value : knownSuccess,
                condition
            );
        }

        var arguments = new FlowValue[property.Property.Parameters.Length];

        foreach (var argument in property.Arguments)
            if (argument.Parameter is { } parameter)
                arguments[parameter.Ordinal] = EvaluateOperation(argument.Value, state);

        return property.Property.GetMethod is { } getter
            ? InvokeMethod(_types.Apply(getter), arguments, [], state,
                getter.IsVirtual)
            : UnknownValue(property.Type);
    }

    private FlowValue CreateFailure(FlowValue errorValue, FlowState state, bool terminate = true)
    {
        var resultValue = FlowValue.Rail(
            errorValue.Kind == ValueKind.Error ? errorValue.Errors : ErrorSet.Of(_librarySymbols.Error, false),
            false
        ).WithDependencyFrom(errorValue, errorValue.Projection == ParameterProjection.RailToError
            ? ParameterProjection.RailErrorsToFailure : ParameterProjection.ErrorToFailure);

        if (resultValue.IsBottom && terminate)
            state.Reachable = false;

        return resultValue;
    }

    private static FlowValue CreateSuccess(FlowValue abstractValue, FlowState state, bool terminate = true)
    {
        if (abstractValue is not { Kind: ValueKind.Error, Errors.IsEmpty: true, MayBeNull: true })
            return FlowValue.Rail(ErrorSet.Empty, true);

        if (terminate)
            state.Reachable = false;

        return FlowValue.Rail(ErrorSet.Empty, false);
    }

    private FlowValue ReadValue(Slot slot, ITypeSymbol? valueType, FlowState state) =>
        state.TryGet(slot, out var abstractValue) && abstractValue.Kind != ValueKind.Other
            ? abstractValue
            : UnknownValue(valueType);

    private FlowValue ForgetTarget(IOperation target, FlowState state)
    {
        var abstractValue = UnknownValue(target.Type);

        if (GetSlot(target) is not { } slot)
            return abstractValue;

        if (IsReferenceStorage(slot))
            InvalidateTrackedValues(state);

        state.Write(slot, abstractValue);

        return abstractValue;
    }

    private static bool IsReferenceStorage(Slot slot) =>
        slot.Symbol is ILocalSymbol { RefKind: not RefKind.None } or IParameterSymbol { RefKind: not RefKind.None };

    // Invalidate locals, parameters and captures together. The shared counter also
    // notifies enclosing callback analyses that their caller state may be stale.
    private void InvalidateTrackedValues(FlowState state)
    {
        _analysisSession.EffectInvalidations++;
        _analysisSession.Limitations.Add(new ErrorFlowLimitation(
            ErrorFlowLimitationCode.CallbackEffectsUntracked, "Callback or local-function effects are not fully tracked"));

        state.Forget(symbol => UnknownValue(symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null
        }));
    }

    // Unknown constructors can invoke delegates just like unknown methods. Keep
    // the same invalidation rule for both, including previously escaped callbacks.
    private bool MayInvokeCallbacks(ImmutableArray<IArgumentOperation> arguments) =>
        _analysisSession.HasMutableCallbacks
        || arguments.Any(argument => argument.Value.Type?.TypeKind == TypeKind.Delegate);

    private void RegisterCallback(IOperation operation)
    {
        // Local-function bodies are not interpreted here. A method group may
        // capture tracked slots, so registering it must retain possible effects.
        if (operation is IMethodReferenceOperation { Method.MethodKind: MethodKind.LocalFunction })
        {
            _analysisSession.HasMutableCallbacks = true;

            return;
        }

        if (operation is not IFlowAnonymousFunctionOperation lambda)
            return;

        var controlFlowGraph = _controlFlowGraph.GetAnonymousFunctionControlFlowGraph(
            lambda, _analysisSession.Cancellation
        );

        _analysisSession.HasMutableCallbacks |= HasCapturedEffects(controlFlowGraph, lambda.Symbol);
    }

    // These writes may affect values owned by the caller. Until callback effects can
    // be propagated precisely, detecting any such operation requires invalidation.
    private static bool HasCapturedEffects(ControlFlowGraph controlFlowGraph, IMethodSymbol owningMethod)
    {
        foreach (var operation in EnumerateGraphOperations(controlFlowGraph))
        {
            var hasCapturedEffect = operation switch
            {
                ISimpleAssignmentOperation assignment => IsCaptured(assignment.Target, owningMethod),
                ICompoundAssignmentOperation assignment => IsCaptured(assignment.Target, owningMethod),
                IIncrementOrDecrementOperation increment => IsCaptured(increment.Target, owningMethod),
                IDeconstructionAssignmentOperation assignment =>
                    EnumerateOperations(assignment.Target).Any(target => IsCaptured(target, owningMethod)),
                IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument =>
                    IsCaptured(argument.Value, owningMethod),
                IInvocationOperation invocation =>
                    invocation.TargetMethod.MethodKind is MethodKind.LocalFunction or MethodKind.DelegateInvoke,
                _ => false
            };

            if (hasCapturedEffect)
                return true;
        }

        return false;
    }

    // Re-evaluating callbacks with captured writes needs an effect analysis, deferred here.
    private static bool IsCaptured(IOperation operation, IMethodSymbol owningMethod) =>
        GetSlot(operation)?.Symbol is { } symbol
        && !SymbolEqualityComparer.Default.Equals(symbol.ContainingSymbol, owningMethod);

    private static FlowValue GetConstantValue(IOperation operation) => operation.ConstantValue.Value switch
    {
        bool boolean => FlowValue.Bool(boolean),
        null => FlowValue.Null,
        _ => FlowValue.Other
    };

    private static Slot? GetSlot(IOperation operation) => operation switch
    {
        ILocalReferenceOperation local => new Slot(local.Local),
        IParameterReferenceOperation parameter => new Slot(parameter.Parameter),
        IFlowCaptureReferenceOperation capture => new Slot(capture.Id),
        IDeclarationExpressionOperation declaration => GetSlot(declaration.Expression),
        IConversionOperation { OperatorMethod: null } conversion => GetSlot(conversion.Operand),
        _ => null
    };

    private static IEnumerable<IOperation> EnumerateGraphOperations(ControlFlowGraph controlFlowGraph)
    {
        foreach (var basicBlock in controlFlowGraph.Blocks)
        {
            var rootOperations = basicBlock.BranchValue is { } branchValue
                ? basicBlock.Operations.Add(branchValue)
                : basicBlock.Operations;

            foreach (var rootOperation in rootOperations)
            foreach (var operation in EnumerateOperations(rootOperation))
                yield return operation;
        }
    }

    private static IEnumerable<IOperation> EnumerateOperations(IOperation operation)
    {
        yield return operation;

        foreach (var childOperation in operation.ChildOperations)
        foreach (var descendantOperation in EnumerateOperations(childOperation))
            yield return descendantOperation;
    }

    private sealed class CallApproximation
    {
        internal IMethodSymbol Method { get; }
        internal FlowValue[] Arguments { get; }
        internal FlowValue Value { get; set; } = FlowValue.Bottom;
        internal bool Active { get; set; }

        internal CallApproximation(IMethodSymbol method, FlowValue[] arguments) =>
            (Method, Arguments) = (method, arguments);
    }

    private sealed class AnalysisSession
    {
        internal CancellationToken Cancellation { get; }
        internal HashSet<ErrorFlowLimitation> Limitations { get; set; } = [];
        internal List<CallApproximation> Approximations { get; } = [];
        internal bool ApproximationChanged { get; set; }
        internal bool UsedRecursion { get; set; }

        internal CallApproximation GetApproximation(IMethodSymbol method, FlowValue[] arguments)
        {
            var normalizedArguments = arguments.Select(value => (value ?? FlowValue.Other).WithoutFlowLinks()).ToArray();
            var existing = Approximations.FirstOrDefault(entry =>
                SymbolEqualityComparer.Default.Equals(entry.Method, method)
                && entry.Arguments.Length == normalizedArguments.Length
                && entry.Arguments.Select((value, argumentIndex) => value.SameAs(normalizedArguments[argumentIndex])).All(equal => equal));

            if (existing is not null)
                return existing;

            var created = new CallApproximation(method, normalizedArguments);
            Approximations.Add(created);

            return created;
        }

        internal void UpdateApproximation(CallApproximation entry, FlowValue returnedValue)
        {
            var joined = entry.Value.Join(returnedValue);

            if (entry.Value.SameAs(joined))
                return;

            entry.Value = joined;
            ApproximationChanged = true;
        }
        internal bool UnresolvedConditions { get; set; }
        internal bool HasMutableCallbacks { get; set; }
        internal int BlockVisits { get; set; }
        internal int ControlFlowDepth { get; set; }
        internal int EffectInvalidations { get; set; }

        internal AnalysisSession(CancellationToken cancellationToken) => Cancellation = cancellationToken;
    }
}
