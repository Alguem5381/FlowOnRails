using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.Analysis.Internal;

internal sealed partial class MethodFlowAnalysis
{
    private ControlFlowGraph? GetCalledGraph(IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.LocalFunction)
            return _methodBodies.GetGraph(method, _analysisSession.Cancellation);

        for (var graph = _controlFlowGraph; graph is not null; graph = graph.Parent)
            foreach (var local in graph.LocalFunctions)
                if (SymbolEqualityComparer.Default.Equals(local.OriginalDefinition, method.OriginalDefinition))
                    return graph.GetLocalFunctionControlFlowGraph(local, _analysisSession.Cancellation);

        return null;
    }

    // Each possible delegate target starts from the same pre-call state. Join only
    // normal completions: a target that throws does not overwrite another target's
    // continuation. Unknown targets retain an open return and invalidate effects.
    private FlowValue InvokeCallable(
        FlowValue callable,
        FlowValue[] arguments,
        Dictionary<int, ImmutableArray<Slot>> referenceArguments,
        FlowState state,
        ITypeSymbol? returnType)
    {
        var returnedValue = FlowValue.Bottom;
        FlowState? completedState = null;

        foreach (var target in callable.Targets)
        {
            var targetState = state.Copy();
            var value = InvokeMethod(target.Method, arguments, referenceArguments, targetState,
                target.IsVirtual, target.Graph, target.Types);

            if (!targetState.Reachable)
                continue;

            returnedValue = returnedValue.Join(value);

            if (completedState is null)
                completedState = targetState;
            else
                completedState.Merge(targetState);
        }

        if (callable.Kind != ValueKind.Callable || callable.HasUnknownTarget)
        {
            var unknownState = state.Copy();
            InvalidateTrackedValues(unknownState);
            _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                ErrorFlowLimitationCode.DelegateTargetUnknown, "The delegate target is not fully known"));
            returnedValue = returnedValue.Join(UnknownValue(returnType));

            if (completedState is null)
                completedState = unknownState;
            else
                completedState.Merge(unknownState);
        }

        if (completedState is null)
            state.Reachable = false;
        else
            state.Adopt(completedState);

        return returnedValue;
    }

    // Calls have separate CFG storage but share captured symbol cells and explicit
    // ref/out aliases. Export those cells only from normal exits, after finally.
    // Heap storage and escaped closure environments remain conservative. Unknown
    // dispatch never closes an interface/virtual contract or preserves stale cells.
    private FlowValue InvokeMethod(
        IMethodSymbol method,
        FlowValue[] arguments,
        Dictionary<int, ImmutableArray<Slot>> referenceArguments,
        FlowState state,
        bool isVirtual = false,
        ControlFlowGraph? graph = null,
        TypeSubstitution? types = null)
    {
        if (method.ContainingType.TypeKind == TypeKind.Interface || method.IsAbstract
            || isVirtual && !method.IsSealed && !method.ContainingType.IsSealed)
        {
            _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                ErrorFlowLimitationCode.OpenDispatch, "Interface or virtual dispatch may return additional errors"));

            return UnknownCall();
        }

        if (method.ReturnsByRef || method.ReturnsByRefReadonly
            || referenceArguments.Any(argument => argument.Value.IsEmpty))
        {
            _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                ErrorFlowLimitationCode.ReferenceEffectsUntracked, "This call uses reference storage whose effects cannot be tracked"));

            if (referenceArguments.Any(argument => argument.Value.IsEmpty))
                InvalidateTrackedValues(state);

            return UnknownCall();
        }

        if (_analysisSession.ControlFlowDepth >= MaximumCallDepth || _analysisSession.Approximations.Count >= 256)
        {
            _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                ErrorFlowLimitationCode.AnalysisBudgetExceeded, "This call could not be fully analyzed; additional errors may be returned"));

            return UnknownCall();
        }

        var approximation = _analysisSession.GetApproximation(method, arguments);

        if (approximation.Active)
        {
            if (referenceArguments.Count > 0 || _analysisSession.HasMutableCallbacks
                || arguments.Any(value => value?.Kind == ValueKind.Callable)
                || method.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction)
            {
                _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                    ErrorFlowLimitationCode.ReferenceEffectsUntracked, "Recursive captured or reference effects require an open result"));
                InvalidateTrackedValues(state);

                return UnknownCall();
            }

            _analysisSession.UsedRecursion = true;

            if (approximation.Value.IsBottom)
                state.Reachable = false;

            return approximation.Value;
        }

        graph ??= GetCalledGraph(method);

        if (graph is null)
        {
            var metadata = _methodBodies.Metadata.Read(method, arguments, out var metadataLimitation);

            if (metadata is not null)
            {
                var referenceStorage = referenceArguments.Values.SelectMany(storage => storage).ToArray();

                // Per-parameter metadata cannot express two parameters denoting
                // the same cell. Independent output summaries would lose write order.
                if (referenceStorage.Distinct().Count() != referenceStorage.Length)
                {
                    _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                        ErrorFlowLimitationCode.ReferenceEffectsUntracked, "Aliased reference arguments need a body to preserve write order"));

                    return UnknownCall();
                }

                _analysisSession.UnresolvedConditions |= metadata.UnresolvedConditions;
                _analysisSession.Limitations.UnionWith(metadata.Limitations);

                if (referenceArguments.Count > 0 && _exceptionBlock is not null)
                {
                    var exceptionalState = state.Copy();

                    foreach (var argument in referenceArguments)
                    foreach (var slot in argument.Value)
                        exceptionalState.Write(slot, _librarySymbols.Unknown(method.Parameters[argument.Key].Type));

                    ObservePossibleException(exceptionalState);
                }

                if (_analysisSession.HasMutableCallbacks || arguments.Any(value => value?.Kind == ValueKind.Callable))
                    InvalidateTrackedValues(state);

                foreach (var argument in referenceArguments)
                {
                    var parameter = method.Parameters[argument.Key];

                    if (parameter.RefKind is not (RefKind.Ref or RefKind.Out))
                        continue;

                    var output = argument.Key < metadata.ParameterOutputs.Length
                        ? metadata.ParameterOutputs[argument.Key] : _librarySymbols.Unknown(parameter.Type);

                    foreach (var slot in argument.Value)
                        state.Write(slot, output.WithoutFlowLinks());
                }

                if (metadata.ReturnValue.IsBottom)
                    state.Reachable = false;

                return metadata.ReturnValue;
            }

            if (_librarySymbols.IsTrackedReturn(method.ReturnType) || referenceArguments.Count > 0)
                _analysisSession.Limitations.Add(metadataLimitation ?? new ErrorFlowLimitation(
                    ErrorFlowLimitationCode.BodyUnavailable, "Called method body is unavailable in this compilation"));

            return UnknownCall();
        }

        var definition = method.OriginalDefinition.PartialImplementationPart ?? method.OriginalDefinition;
        var capturedSlots = state.SymbolSlots
            .Where(slot => !SymbolEqualityComparer.Default.Equals(slot.Symbol!.ContainingSymbol, definition))
            .Distinct().ToArray();
        var entryState = state.CopySymbols();
        var asynchronousCaptures = method.IsAsync
            && (arguments.Any(value => value?.Kind == ValueKind.Callable)
                || EnumerateGraphOperations(graph).Select(GetSlot).Any(slot =>
                    slot?.Symbol is ILocalSymbol or IParameterSymbol
                    && !SymbolEqualityComparer.Default.Equals(slot.Value.Symbol!.ContainingSymbol, definition)));

        if (asynchronousCaptures)
        {
            // The delegate can resume after its caller changes captured cells.
            // An invocation cannot commit post-await effects as synchronous writes.
            _analysisSession.HasMutableCallbacks = true;
            _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                ErrorFlowLimitationCode.AsyncCapturedStateUntracked, "Asynchronous captured state is not tracked precisely"));
            InvalidateTrackedValues(entryState);
        }

        foreach (var parameter in definition.Parameters)
        {
            var slot = new Slot(parameter);

            if (referenceArguments.TryGetValue(parameter.Ordinal, out var storage))
                entryState.BindAlias(slot, storage);
            else
                entryState.Write(slot, parameter.Ordinal < arguments.Length && arguments[parameter.Ordinal] is { } argument
                    ? argument.WithoutFlowLinks() : UnknownValue(parameter.Type));
        }

        if (referenceArguments.Count > 0 || _analysisSession.HasMutableCallbacks
            || arguments.Any(value => value?.Kind == ValueKind.Callable)
            || method.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction
            && HasCapturedEffects(graph, definition))
            ObserveCallExceptionEffects();

        var previousInvalidations = _analysisSession.EffectInvalidations;
        var calledAnalysis = new MethodFlowAnalysis(_librarySymbols, _methodBodies, graph, method, _analysisSession,
            types ?? new TypeSubstitution(_librarySymbols.Compilation, method, _types),
            method.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction ? _invocationResults : null,
            method.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction ? _observation : null);
        approximation.Active = true;

        try
        {
            var returnedValue = calledAnalysis.AnalyzeMethodReturn(entryState) ?? FlowValue.Bottom;

            if (_analysisSession.EffectInvalidations != previousInvalidations)
                InvalidateTrackedValues(state);

            if (calledAnalysis._controlFlowIncomplete)
                InvalidateTrackedValues(state);
            else if (asynchronousCaptures)
                InvalidateTrackedValues(state);
            else if (calledAnalysis._normalExitState is { } exitState)
            {
                foreach (var slot in capturedSlots.Concat(referenceArguments.Values.SelectMany(storage => storage)).Distinct())
                    if (exitState.TryGet(slot, out var value)
                        && (!state.TryGet(slot, out var oldValue)
                            || !oldValue.WithoutFlowLinks().SameAs(value.WithoutFlowLinks())))
                        state.Write(slot, value.WithoutFlowLinks());
            }
            else if (!method.IsAsync)
                state.Reachable = false;

            // A closure escaping its declaring call needs an environment identity.
            // Do not read a later invocation's parameters as that earlier environment.
            if (returnedValue.Targets.Any(target => target.Graph is { } targetGraph
                    && EnumerateGraphOperations(targetGraph).Select(GetSlot).Any(slot =>
                        SymbolEqualityComparer.Default.Equals(slot?.Symbol?.ContainingSymbol, definition))))
            {
                _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                    ErrorFlowLimitationCode.EscapingClosure, "Escaping closure environments are not tracked"));
                returnedValue = FlowValue.Callable([], unknown: true);
            }

            if (_librarySymbols.IsTrackedReturn(method.ReturnType)
                && !returnedValue.IsBottom
                && !_librarySymbols.MatchesReturnType(method.ReturnType, returnedValue))
            {
                _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                    ErrorFlowLimitationCode.ReturnValueUnrepresented, "The called method's returned value could not be represented"));
                returnedValue = _librarySymbols.Unknown(method.ReturnType);
            }

            _analysisSession.UpdateApproximation(approximation, returnedValue);

            return returnedValue.WithoutFlowLinks();
        }
        finally
        {
            approximation.Active = false;
        }

        FlowValue UnknownCall()
        {
            if (referenceArguments.Any(argument => argument.Value.IsEmpty))
                InvalidateTrackedValues(state);

            foreach (var argument in referenceArguments)
                if (method.Parameters[argument.Key].RefKind is RefKind.Ref or RefKind.Out)
                    foreach (var slot in argument.Value)
                        state.Write(slot, _librarySymbols.Unknown(method.Parameters[argument.Key].Type));

            if (_analysisSession.HasMutableCallbacks
                || method.MethodKind is MethodKind.LocalFunction or MethodKind.DelegateInvoke
                || arguments.Any(value => value?.Kind == ValueKind.Callable))
                InvalidateTrackedValues(state);

            return _librarySymbols.IsTrackedReturn(method.ReturnType)
                ? _librarySymbols.OpenContract(method) : _librarySymbols.Unknown(method.ReturnType);
        }

        void ObserveCallExceptionEffects()
        {
            if (_exceptionBlock is null)
                return;

            var exceptionalState = state.Copy();

            foreach (var slot in capturedSlots.Concat(referenceArguments.Values.SelectMany(storage => storage)).Distinct())
                exceptionalState.Write(slot, UnknownValue(slot.Symbol switch
                {
                    ILocalSymbol local => local.Type,
                    IParameterSymbol parameter => parameter.Type,
                    _ => null
                }));

            ObservePossibleException(exceptionalState);
        }
    }
}
