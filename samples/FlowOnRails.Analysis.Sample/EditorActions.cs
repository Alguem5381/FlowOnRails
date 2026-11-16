using FlowOnRails;

namespace FlowOnRails.Analysis.Sample;

public interface IEditorActionsReader
{
    RailFor<int> Read(bool denied);
}

public sealed class EditorActionsReader : IEditorActionsReader
{
    // On this declaration's FORFLOW001, invoke the ResultsWith code fix.
    // The interface stays open after UserNotFound and AccessDenied are declared.
    public RailFor<int> Read(bool denied) => denied ? new AccessDenied() : new UserNotFound(42);
}

public static class EditorActions
{
    public static int Handle(IUserReader reader)
    {
        if (reader.Read().TryUnwrap(out var value, out var error))
            return value;

        // Invoke the code fix on "switch" to add known error cases.
        // Generated bodies throw NotImplementedException until implemented.
        // Keep the fallback: other implementations may return other errors.
        return error switch
        {
            UserNotFound => -1,
            _ => -2
        };
    }

    public static void SuccessValues()
    {
        _ = Rail.Ok(42);
        _ = Rail.Ok(new AccessDenied()); // Info: Error is a success value.
        _ = Rail.Fail<int>(new AccessDenied()); // Info: possible failure types.
    }
}
