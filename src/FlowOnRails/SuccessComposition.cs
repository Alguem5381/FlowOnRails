namespace FlowOnRails;

internal static class SuccessComposition
{
    internal static Success? Combine(Success? current, Success? next) => next ?? current;
}