using FlowOnRails;

namespace HttpProducer;

public sealed record ProducerUnmapped : Error;

public static class Handlers
{
    public static Rail Read() => new ProducerUnmapped();
}
