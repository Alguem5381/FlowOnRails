using FlowOnRails;
namespace Producer;
public sealed record Missing : Error;
public static class Api
{
    public static RailFor<int> Read() => new Missing();
}
