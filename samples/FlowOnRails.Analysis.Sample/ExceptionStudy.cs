using System.IO;

using FlowOnRails;

namespace FlowOnRails.Analysis.Sample;

public static class ExceptionStudy
{
    // Info includes the returned UserNotFound and the mapped PublicFailure.
    // IOException is an exception path, not an automatically returned Error type.
    public static RailFor<int> MapException()
    {
        try
        {
            _ = File.ReadAllText("user.txt");

            return new UserNotFound(42);
        }
        catch (IOException)
        {
            return new PublicFailure();
        }
    }

    // The return is evaluated before finally: AccessDenied is still returned.
    public static RailFor<int> PreservePendingReturn()
    {
        RailFor<int> result = new AccessDenied();

        try
        {
            return result;
        }
        finally
        {
            result = new PublicFailure();
        }
    }

    // A read after finally sees the replacement instead: PublicFailure.
    public static RailFor<int> ReadAfterFinally()
    {
        RailFor<int> result = new AccessDenied();

        try
        {
            result = new UserNotFound(42);
        }
        finally
        {
            result = new PublicFailure();
        }

        return result;
    }

    // Neither a domain error nor a success can leave through the pending return.
    public static RailFor<int> InterruptedReturn()
    {
        try
        {
            return new AccessDenied();
        }
        finally
        {
            throw new IOException("Cleanup failed.");
        }
    }
}
