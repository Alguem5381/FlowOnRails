using System;
using System.Threading.Tasks;

using FlowOnRails;

[AttributeUsage(AttributeTargets.Class)]
public sealed class AuditControllerAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class MapsErrorAttribute(Type errorType) : Attribute
{
    public Type ErrorType { get; } = errorType;
}

public sealed record KnownFailure : Error;

// This project makes the compiler discover and load the satellite analyzer.
// The application has no reference to FlowOnRails.Analysis or Roslyn.
[AuditController]
public sealed class CompilerController
{
    public RailFor<int> MissingMapping() => new KnownFailure();
    public Task<RailFor<int>> AsyncMissingMapping() => Task.FromResult(Rail.Fail<int>(new KnownFailure()));
    public RailFor<Error> SuccessfulError() => Rail.Ok<Error>(new KnownFailure());
}
