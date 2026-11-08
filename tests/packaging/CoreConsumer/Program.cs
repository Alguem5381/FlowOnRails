using FlowOnRails;

var success = Rail.Ok(42);

if (!success.TryUnwrap(out var value, out _) || value != 42)
    throw new InvalidOperationException("The packaged rail did not preserve its value.");

var expected = new Missing();
var failure = Rail.Fail<int>(expected);

if (failure.TryUnwrap(out _, out var error) || !ReferenceEquals(expected, error))
    throw new InvalidOperationException("The packaged rail did not preserve its error.");

Console.WriteLine("Core package: success and failure passed.");

public sealed record Missing : Error;
