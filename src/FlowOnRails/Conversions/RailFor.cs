namespace FlowOnRails;

public readonly partial struct RailFor<TValue> where TValue : notnull
{
    /// <summary>
    /// Implicitly converts a typed rail to a non-typed rail.
    /// </summary>
    /// <param name="rail">The typed rail to convert.</param>
    public static implicit operator Rail(RailFor<TValue> rail) =>
        rail.IsSuccess
            ? Rail.Ok()
            : Rail.Fail(rail.Error!);
}
