using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.Analysis.Internal;

internal sealed partial class MethodFlowAnalysis
{
    private readonly List<ControlFlowFrame> _controlFlowFrames = [];
    private BasicBlock? _exceptionBlock;
    private FlowValue? _controlFlowReturn;
    private bool _controlFlowIncomplete;
    private bool _controlFlowBudgetExceeded;
    private FlowState? _normalExitState;

    // Ordinary CFG edges omit exceptional successors and bypass finally bodies.
    // Interpret each finally in its own frame before resuming the pending edge;
    // never merge that edge's return value with assignments made by the finally.
    // Potentially throwing operations independently seed enclosing handlers from
    // snapshots of local state. Exceptions themselves are not domain Error values.
    // A handler runs only after inner finally blocks unwind, and a throw in a finally
    // abandons the previous transfer. All frames share the bounded work budget.
    private FlowValue? AnalyzeControlFlow(FlowState entryState)
    {
        ExecuteRegion(_controlFlowGraph.Root, entryState);

        return _controlFlowIncomplete
            ? _controlFlowReturn?.Join(UnknownValue(BodyReturnType)) ?? UnknownValue(BodyReturnType)
            : _controlFlowReturn;
    }

    private FlowState? ExecuteRegion(ControlFlowRegion region, FlowState entryState)
    {
        if (_analysisSession.ControlFlowDepth >= MaximumCallDepth)
        {
            _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                ErrorFlowLimitationCode.AnalysisBudgetExceeded, "Control-flow nesting limit exceeded"));
            _controlFlowIncomplete = true;

            return null;
        }

        var frame = new ControlFlowFrame(region);
        frame.Enqueue(region.FirstBlockOrdinal, entryState);
        _controlFlowFrames.Add(frame);
        _analysisSession.ControlFlowDepth++;
        var previousExceptionBlock = _exceptionBlock;

        try
        {
            while (frame.PendingBlocks.Count > 0 && !_controlFlowBudgetExceeded)
            {
                _analysisSession.Cancellation.ThrowIfCancellationRequested();

                if (++_analysisSession.BlockVisits > MaximumBlockVisits)
                {
                    _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                        ErrorFlowLimitationCode.AnalysisBudgetExceeded, "Analysis budget exceeded"));
                    _controlFlowIncomplete = true;
                    _controlFlowBudgetExceeded = true;
                    break;
                }

                var (blockOrdinal, blockState) = frame.Dequeue();
                var basicBlock = _controlFlowGraph.Blocks[blockOrdinal];
                _exceptionBlock = HasExceptionScope(basicBlock.EnclosingRegion) ? basicBlock : null;

                foreach (var operation in basicBlock.Operations)
                {
                    EvaluateOperation(operation, blockState);

                    if (!blockState.Reachable)
                        break;
                }

                if (!blockState.Reachable)
                    continue;

                var branchValue = basicBlock.BranchValue is { } branchExpression
                    ? EvaluateOperation(branchExpression, blockState)
                    : FlowValue.Other;

                if (!blockState.Reachable)
                    continue;

                // Refine sibling paths independently, before their own finally blocks.
                if (basicBlock.ConditionKind != ControlFlowConditionKind.None)
                {
                    var conditionalTruth = basicBlock.ConditionKind == ControlFlowConditionKind.WhenTrue;
                    FollowBranch(basicBlock.ConditionalSuccessor,
                        ConstrainBranch(blockState.Copy(), branchValue, conditionalTruth), branchValue, frame);
                    FollowBranch(basicBlock.FallThroughSuccessor,
                        ConstrainBranch(blockState.Copy(), branchValue, !conditionalTruth), branchValue, frame);

                    continue;
                }

                if (basicBlock.Kind == BasicBlockKind.Exit && _methodSymbol.ReturnsVoid)
                    AddMethodReturn(FlowValue.Other, blockState);

                FollowBranch(basicBlock.FallThroughSuccessor, blockState, branchValue, frame);
            }

            return frame.ExitState;
        }
        finally
        {
            _exceptionBlock = previousExceptionBlock;
            _controlFlowFrames.RemoveAt(_controlFlowFrames.Count - 1);
            _analysisSession.ControlFlowDepth--;
        }
    }

    private void FollowBranch(ControlFlowBranch? branch, FlowState state, FlowValue value, ControlFlowFrame frame)
    {
        if (branch is null || !state.Reachable)
            return;

        if (branch.Semantics is ControlFlowBranchSemantics.Throw or ControlFlowBranchSemantics.Rethrow)
        {
            DispatchException(branch.Source, state);

            return;
        }

        var completedState = ExecuteFinallyRegions(branch.FinallyRegions, state);

        if (completedState is null)
            return;

        if (branch.Semantics == ControlFlowBranchSemantics.Return)
        {
            // Return expressions are evaluated before finally. A copied rail must
            // keep its old value even if its source local is subsequently changed.
            AddMethodReturn(value, completedState);

            return;
        }

        if (branch.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling
            && branch.Destination is null
            && frame.Region.Kind == ControlFlowRegionKind.Finally)
        {
            frame.Complete(completedState);

            return;
        }

        if (branch.Destination is { IsReachable: true } destination
            && branch.Semantics is ControlFlowBranchSemantics.Regular or ControlFlowBranchSemantics.StructuredExceptionHandling)
            EnqueueBlock(destination.Ordinal, completedState);
    }

    private void AddMethodReturn(FlowValue value, FlowState state)
    {
        _controlFlowReturn = _controlFlowReturn is null ? value : _controlFlowReturn.Join(value);

        if (_normalExitState is null)
            _normalExitState = state.Copy();
        else
            _normalExitState.Merge(state);
    }

    private FlowState? ExecuteFinallyRegions(IEnumerable<ControlFlowRegion> finallyRegions, FlowState state)
    {
        FlowState? completedState = state;

        foreach (var finallyRegion in finallyRegions)
        {
            completedState = ExecuteRegion(finallyRegion, completedState);

            if (completedState is null)
                return null;
        }

        return completedState;
    }

    private void EnqueueBlock(int blockOrdinal, FlowState state)
    {
        for (var frameIndex = _controlFlowFrames.Count - 1; frameIndex >= 0; frameIndex--)
        {
            var frame = _controlFlowFrames[frameIndex];

            if (blockOrdinal < frame.Region.FirstBlockOrdinal || blockOrdinal > frame.Region.LastBlockOrdinal)
                continue;

            frame.Enqueue(blockOrdinal, state);

            return;
        }
    }

    private void ObservePossibleException(FlowState state)
    {
        if (_exceptionBlock is null || _controlFlowBudgetExceeded)
            return;

        var exceptionState = state.Copy();
        exceptionState.Reachable = true;
        DispatchException(_exceptionBlock, exceptionState);
    }

    // Exception types and call effects are not exported as throw summaries yet.
    // Include compatible handler possibilities conservatively, stopping at an
    // unconditional catch-all. A catch is outside its own protected try, so throws
    // and rethrows in that handler search outward rather than re-entering it.
    private void DispatchException(BasicBlock source, FlowState state)
    {
        var finallyRegions = new List<ControlFlowRegion>();

        for (var region = source.EnclosingRegion;
             region is not null && !ReferenceEquals(region, _controlFlowGraph.Root);
             region = region.EnclosingRegion)
        {
            if (region.Kind != ControlFlowRegionKind.Try || region.EnclosingRegion is not { } enclosingRegion)
                continue;

            if (enclosingRegion.Kind == ControlFlowRegionKind.TryAndFinally)
            {
                finallyRegions.Add(enclosingRegion.NestedRegions.Single(nested => nested.Kind == ControlFlowRegionKind.Finally));
                continue;
            }

            if (enclosingRegion.Kind != ControlFlowRegionKind.TryAndCatch)
                continue;

            _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                ErrorFlowLimitationCode.ExceptionFlowApproximation, "Possible exception paths are considered conservatively"));
            var handlers = enclosingRegion.NestedRegions.Where(nested => nested.Kind is
                ControlFlowRegionKind.Catch or ControlFlowRegionKind.FilterAndHandler).ToArray();

            // Filters execute during handler search, before finally unwinding, and
            // can mutate locals even when rejected. Preserve known body returns,
            // but widen the summary and handler state until those effects are modeled.
            if (handlers.Any(handler => handler.Kind == ControlFlowRegionKind.FilterAndHandler))
            {
                _analysisSession.Limitations.Add(new ErrorFlowLimitation(
                    ErrorFlowLimitationCode.ExceptionFilterEffectsUntracked, "Exception filter effects are not fully tracked"));
                _controlFlowIncomplete = true;
                _analysisSession.HasMutableCallbacks = true;
                state = state.Copy();
                state.Forget(symbol => UnknownValue(symbol switch
                {
                    ILocalSymbol local => local.Type,
                    IParameterSymbol parameter => parameter.Type,
                    _ => null
                }));
            }

            foreach (var handler in handlers)
            {
                var handlerState = ExecuteFinallyRegions(finallyRegions, state.Copy());
                var handlerBody = handler.Kind == ControlFlowRegionKind.FilterAndHandler
                    ? handler.NestedRegions.Single(nested => nested.Kind == ControlFlowRegionKind.Catch)
                    : handler;

                if (handlerState is not null)
                    EnqueueBlock(handlerBody.FirstBlockOrdinal, handlerState);

                if (handler.Kind == ControlFlowRegionKind.Catch && IsCatchAll(handler.ExceptionType))
                    return;
            }
        }

        // Even an escaping exception executes finally. These visits can reveal
        // call-site facts and exceptions caught by a surrounding handler.
        ExecuteFinallyRegions(finallyRegions, state.Copy());
    }

    private bool IsCatchAll(ITypeSymbol? exceptionType) => exceptionType is null
        || exceptionType.SpecialType == SpecialType.System_Object
        || SymbolEqualityComparer.Default.Equals(exceptionType,
            _librarySymbols.Compilation.GetTypeByMetadataName("System.Exception"));

    private bool HasExceptionScope(ControlFlowRegion region)
    {
        // Anonymous graphs retain enclosing regions from their lexical parent.
        // Exception dispatch crosses that boundary only through the invocation;
        // block ordinals in the two graphs are unrelated.
        for (ControlFlowRegion? enclosing = region;
             enclosing is not null && !ReferenceEquals(enclosing, _controlFlowGraph.Root);
             enclosing = enclosing.EnclosingRegion)
            if (enclosing.Kind == ControlFlowRegionKind.Try)
                return true;

        return false;
    }

    // Pure storage operations delegate to their children, which are observed at
    // the actual evaluation point. In particular, a constructor can throw before
    // an assignment writes its target. Unknown operations remain potentially throwing.
    private static bool MayThrow(IOperation operation) => operation switch
    {
        ILiteralOperation or IDefaultValueOperation or ILocalReferenceOperation or IParameterReferenceOperation
            or IFlowCaptureReferenceOperation or IFlowCaptureOperation or IArgumentOperation
            or IDeclarationExpressionOperation or IVariableDeclarationGroupOperation or IVariableDeclarationOperation
            or IVariableDeclaratorOperation or IExpressionStatementOperation or IReturnOperation
            or ICaughtExceptionOperation or IThrowOperation or IIsNullOperation or IIsTypeOperation
            or IFlowAnonymousFunctionOperation or ILocalFunctionOperation => false,
        ISimpleAssignmentOperation assignment => GetSlot(assignment.Target) is null,
        IConversionOperation conversion => conversion.OperatorMethod is not null
            || !conversion.Conversion.IsIdentity
            && !(conversion.Conversion.IsImplicit
                && (conversion.Conversion.IsReference || conversion.Conversion.IsNumeric)),
        IUnaryOperation unary => unary.OperatorMethod is not null || unary.IsChecked,
        IBinaryOperation binary => binary.OperatorMethod is not null || binary.IsChecked
            || binary.OperatorKind is BinaryOperatorKind.Divide or BinaryOperatorKind.Remainder,
        IIsPatternOperation pattern => pattern.Pattern.DescendantsAndSelf().Any(nested => nested is IRecursivePatternOperation),
        _ => true
    };

    // These operations can run user code outside the ordinary invocation/getter
    // models. A registered callback may change tracked locals before an exception
    // or a normal result, so neither path may retain the old facts.
    private bool HasUnmodeledEffects(IOperation operation) => operation switch
    {
        IConversionOperation { OperatorMethod: { } conversion } => !_librarySymbols.IsRail(conversion.ContainingType),
        IBinaryOperation { OperatorMethod: not null } or IUnaryOperation { OperatorMethod: not null } => true,
        IIsPatternOperation pattern => pattern.Pattern.DescendantsAndSelf().Any(nested => nested is IRecursivePatternOperation),
        IFieldReferenceOperation { Field.IsStatic: true } => true,
        IDynamicInvocationOperation or IDynamicMemberReferenceOperation
            or IDynamicObjectCreationOperation or IDynamicIndexerAccessOperation => true,
        _ => false
    };

    private sealed class ControlFlowFrame
    {
        internal ControlFlowRegion Region { get; }
        internal Queue<int> PendingBlocks { get; } = new();
        internal FlowState? ExitState { get; private set; }
        private FlowState?[] Inputs { get; }
        private bool[] Queued { get; }

        internal ControlFlowFrame(ControlFlowRegion region)
        {
            Region = region;
            Inputs = new FlowState?[region.LastBlockOrdinal - region.FirstBlockOrdinal + 1];
            Queued = new bool[Inputs.Length];
        }

        internal void Enqueue(int blockOrdinal, FlowState state)
        {
            var blockIndex = blockOrdinal - Region.FirstBlockOrdinal;

            if (Inputs[blockIndex] is { } previousState)
            {
                if (!previousState.Merge(state))
                    return;
            }
            else
                Inputs[blockIndex] = state.Copy();

            if (Queued[blockIndex])
                return;

            Queued[blockIndex] = true;
            PendingBlocks.Enqueue(blockOrdinal);
        }

        internal (int BlockOrdinal, FlowState State) Dequeue()
        {
            var blockOrdinal = PendingBlocks.Dequeue();
            var blockIndex = blockOrdinal - Region.FirstBlockOrdinal;
            Queued[blockIndex] = false;

            return (blockOrdinal, Inputs[blockIndex]!.Copy());
        }

        internal void Complete(FlowState state)
        {
            if (ExitState is null)
                ExitState = state.Copy();
            else
                ExitState.Merge(state);
        }
    }
}
