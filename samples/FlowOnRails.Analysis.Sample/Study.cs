using System;

using FlowOnRails;

namespace FlowOnRails.Analysis.Sample;

public enum Scenario
{
    DirectFailure,
    ImplicitFailure,
    Alias,
    DiscardedError,
    OverwrittenFailure,
    RecoveredByType,
    ConditionalRecovery,
    PropertyPattern,
    ConditionalExpression,
    SwitchExpression,
    Loop,
    MapError,
    RecoverWith,
    SkippedBind,
    ObservedError,
    ConcreteCall,
    UnknownCall
}

public sealed record UserNotFound(int Id) : Error;
public sealed record AccessDenied : Error;
public sealed record PublicFailure : Error;

public interface IUserReader
{
    [ResultsWith<UserNotFound, AccessDenied>]
    RailFor<int> Read();
}

public static class ErrorFlowStudy
{
    // Inspect FORFLOW001 on this declaration. Each scenario is also exercised
    // separately by the analyzer tests so the combined summary cannot hide a bug.
    public static RailFor<int> Study(Scenario scenario, bool useFallback)
    {
        switch (scenario)
        {
            case Scenario.DirectFailure:
                return Rail.Fail<int>(new UserNotFound(1));
            case Scenario.ImplicitFailure:
                return new AccessDenied();
            case Scenario.Alias:
            {
                Error error = new AccessDenied();
                Error alias = error;
                return Rail.Fail<int>(alias);
            }
            case Scenario.DiscardedError:
            {
                var unusedError = new UserNotFound(1);
                return Rail.Ok(42);
            }
            case Scenario.OverwrittenFailure:
            {
                var result = Rail.Fail<int>(new AccessDenied());
                result = Rail.Ok(42);
                return result;
            }
            case Scenario.RecoveredByType:
            {
                var result = Rail.Fail<int>(new UserNotFound(1));
                if (!result.TryUnwrap(out var value, out var error))
                {
                    if (error is UserNotFound)
                        return Rail.Ok(42);

                    return Rail.Fail<int>(error);
                }

                return Rail.Ok(value);
            }
            case Scenario.ConditionalRecovery:
            {
                Error error = new UserNotFound(1);
                if (error is UserNotFound && useFallback)
                    return Rail.Ok(42);

                return Rail.Fail<int>(error);
            }
            case Scenario.PropertyPattern:
            {
                Error error = new UserNotFound(1);
                if (error is UserNotFound { Id: > 0 })
                    return Rail.Ok(42);

                return Rail.Fail<int>(error);
            }
            case Scenario.ConditionalExpression:
                return useFallback ? Rail.Ok(42) : Rail.Fail<int>(new AccessDenied());
            case Scenario.SwitchExpression:
            {
                Error error = useFallback ? new UserNotFound(1) : new AccessDenied();
                return error switch
                {
                    UserNotFound => Rail.Ok(42),
                    _ => Rail.Fail<int>(error)
                };
            }
            case Scenario.Loop:
            {
                var result = Rail.Fail<int>(new AccessDenied());
                while (useFallback)
                {
                    result = Rail.Ok(42);
                    useFallback = false;
                }

                return result;
            }
            case Scenario.MapError:
                return Rail.Fail<int>(new AccessDenied()).MapError(error => new PublicFailure());
            case Scenario.RecoverWith:
                return Rail.Fail<int>(new UserNotFound(1)).RecoverWith(error =>
                    error is UserNotFound ? Rail.Ok(42) : Rail.Fail<int>(error));
            case Scenario.SkippedBind:
                return Rail.Fail<int>(new AccessDenied())
                    .Bind(value => Rail.Fail<int>(new PublicFailure()));
            case Scenario.ObservedError:
                return Rail.Fail<int>(new AccessDenied()).TapError(error => Console.WriteLine(error));
            case Scenario.UnknownCall:
                return ReadThroughInterface();
            case Scenario.ConcreteCall:
                return ReadFromAnotherMethod();
            default:
                return Rail.Ok(42);
        }
    }

    // Concrete calls contribute their returned errors; interface contracts stay open.
    private static RailFor<int> ReadFromAnotherMethod() => new AccessDenied();

    private static RailFor<int> ReadThroughInterface()
    {
        IUserReader reader = new UserReader();

        return reader.Read();
    }

    private sealed class UserReader : IUserReader
    {
        public RailFor<int> Read() => new AccessDenied();
    }
}
