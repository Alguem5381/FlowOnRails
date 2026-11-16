using FlowOnRails;
using Producer;

var expectSummaries = !args.Contains("--disabled", StringComparer.Ordinal);
var hasSummaries = typeof(Api).Assembly.GetCustomAttributes(typeof(FlowAnalysisSummaryAttribute), false).Any();

if (hasSummaries != expectSummaries)
    throw new InvalidOperationException($"Expected producer summaries: {expectSummaries}; found: {hasSummaries}.");

if (Study.Read().IsSuccess)
    throw new InvalidOperationException("The producer must fail.");

#if NET11_0
await ModernStudy.Verify();
#endif

Console.WriteLine($"Packaged generator metadata present: {hasSummaries}.");

public static class Study
{
    public static RailFor<int> Read() => Api.Read();

#if NET11_0
    public static RailFor<int> ReadClosed(bool denied) => ModernStudy.ClosedHierarchy(denied);
    public static RailFor<int> ReadLabeledBreak() => ModernStudy.LabeledBreak();
    public static RailFor<int> ReadLabeledContinue() => ModernStudy.LabeledContinue();
    public static RailFor<int> ReadIndexer() => ModernStudy.ExtensionIndexer();
    public static RailFor<int> ReadCollectionEffects() => ModernStudy.CollectionArgumentEffects();
    public static Task<RailFor<int>> ReadTask() => ModernStudy.TaskFailure();
    public static ValueTask<RailFor<int>> ReadValueTask() => ModernStudy.ValueTaskFailure();
    public static Task<RailFor<int>> ReadTaskBind() => ModernStudy.TaskBind();
    public static ValueTask<RailFor<int>> ReadValueTaskMapError() => ModernStudy.ValueTaskMapError();
    public static RailFor<int> ReadUnion(Outcome outcome) => ModernStudy.UnionFailure(outcome);
    public static RailFor<Outcome> ReadUnionSuccess() => ModernStudy.UnionSuccess();
#endif

    public static int Handle()
    {
        if (Api.Read().TryUnwrap(out var value, out var error))
            return value;

        return error switch { _ => -1 };
    }
}
