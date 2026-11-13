using FlowOnRails;
using FlowOnRails.Extensions.Tasks;

public sealed record Payload(int Value);
public closed record DomainFailure : Error;
public sealed record Missing : DomainFailure;
public sealed record Denied : DomainFailure;
public union Outcome(Payload, Missing);
public sealed class Reader;
public interface IModernReader
{
    [ResultsWith<DomainFailure>]
    RailFor<int> Read();
}

public static class ReaderExtensions
{
    extension(Reader reader)
    {
        public RailFor<int> this[int identifier] => new Missing();
    }
}

public static class ModernStudy
{
    public static RailFor<Outcome> UnionSuccess() => Rail.Ok<Outcome>(new Missing());

    public static RailFor<int> UnionFailure(Outcome outcome) => outcome switch
    {
        Payload payload => Rail.Ok(payload.Value),
        Missing failure => Rail.Fail<int>(failure)
    };

    public static RailFor<int> ClosedHierarchy(bool denied) =>
        denied ? new Denied() : new Missing();

    public static RailFor<int> CollectionArguments()
    {
        List<Error> errors = [with(capacity: 2), new Missing(), new Denied()];

        return errors.Count == 2 ? new Missing() : new Denied();
    }

    public static RailFor<int> CollectionArgumentEffects()
    {
        RailFor<int> result = new Denied();
        List<int> values = [with(capacity: ChangeResult(ref result))];

        return result;
    }

    private static int ChangeResult(ref RailFor<int> result)
    {
        result = new Missing();

        return 1;
    }

    public static RailFor<int> LabeledBreak()
    {
        RailFor<int> result = new Denied();

        outer: while (true)
        {
            while (true)
            {
                result = new Missing();
                break outer;
            }
        }

        return result;
    }

    public static RailFor<int> LabeledContinue()
    {
        RailFor<int> result = new Denied();

        outer: for (var attempt = 0; attempt < 2; attempt++)
        {
            while (true)
            {
                result = new Missing();
                continue outer;
            }
        }

        return result;
    }

    public static RailFor<int> ExtensionIndexer() => new Reader()[42];

    public static async Task<RailFor<int>> TaskFailure()
    {
        await Task.Yield();

        return new Missing();
    }

    public static async ValueTask<RailFor<int>> ValueTaskFailure()
    {
        await Task.Yield();

        return new Denied();
    }

    public static Task<RailFor<int>> TaskBind() =>
        Task.FromResult(Rail.Ok(42)).BindAsync(async value =>
        {
            await Task.Yield();

            return Rail.Fail<int>(new Missing());
        });

    public static ValueTask<RailFor<int>> ValueTaskMapError() =>
        ValueTask.FromResult(Rail.Fail<int>(new Missing())).MapErrorAsync(async error =>
        {
            await Task.Yield();

            return (Error)new Denied();
        });

    public static async Task Verify()
    {
        if (!UnionSuccess().TryUnwrap(out var outcome, out _)
            || outcome is not Missing
            || !UnionFailure(new Missing()).IsFailure
            || !UnionFailure(new Payload(42)).IsSuccess
            || !ClosedHierarchy(true).IsFailure
            || !CollectionArguments().IsFailure
            || !CollectionArgumentEffects().IsFailure
            || !LabeledBreak().IsFailure
            || !LabeledContinue().IsFailure
            || !ExtensionIndexer().IsFailure
            || !(await TaskFailure()).IsFailure
            || !(await ValueTaskFailure()).IsFailure
            || !(await TaskBind()).IsFailure
            || !(await ValueTaskMapError()).IsFailure)
            throw new InvalidOperationException("A C# 15 interaction changed rail success or failure semantics.");

        Console.WriteLine("C# 15 unions, closed errors, collection arguments, labeled jumps, extension indexers and async passed.");
    }
}
