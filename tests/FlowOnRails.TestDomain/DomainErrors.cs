namespace FlowOnRails.TestDomain;

public abstract record DomainError(string Resource) : Error;
public record DomainNotFound(string Resource) : DomainError(Resource);
public sealed record UserNotFound(Guid Id, string? Reason = null) : DomainNotFound("user");
public sealed record ValidationError<T>(T InvalidValue) : DomainError("validation");
public record CodeError : DomainError
{
    public CodeError() : base("code") { }
    public string Code => "base";
}
public sealed record HiddenCodeError : CodeError
{
    public new string Code => "derived";
}
