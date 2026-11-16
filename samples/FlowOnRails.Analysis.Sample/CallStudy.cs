using FlowOnRails;

namespace FlowOnRails.Analysis.Sample;

public static class CallStudy
{
    // The last write performed by the local function reaches the caller's return.
    public static RailFor<int> CapturedWrite()
    {
        RailFor<int> result = new AccessDenied();
        void Replace() => result = new PublicFailure();
        Replace();

        return result;
    }

    // Both identified targets are considered, without opening the delegate's signature.
    public static RailFor<int> KnownTargets(bool first)
    {
        Func<RailFor<int>> read = first
            ? () => new AccessDenied()
            : () => new PublicFailure();

        return read();
    }

    // A delegate parameter can still have arbitrary targets, so this remains open.
    public static RailFor<int> UnknownTarget(Func<RailFor<int>> read) => read();

    public static RailFor<int> ReferenceWrite()
    {
        RailFor<int> result = new AccessDenied();
        ref var alias = ref result;
        Replace(ref alias);

        return result;
    }

    private static void Replace(ref RailFor<int> result) => result = new PublicFailure();

    public static RailFor<int> ReadGetter() => Current;

    private static RailFor<int> Current => new PublicFailure();

    // Only AccessDenied can reach a normal return; recursion may also never finish.
    public static RailFor<int> Recursive(bool stop) => stop ? new AccessDenied() : Recursive(stop);
}
