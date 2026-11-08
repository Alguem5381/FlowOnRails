namespace FlowOnRails.AspNetCore.Sample;

public sealed class UserService
{
    public RailFor<UserDto> Find(int id)
    {
        if (id < 1)
            return new InvalidUserId(id);

        return id == 1
            ? Rail.Ok(new UserDto { Id = id, Name = "Ada" })
            : new UserNotFound(id);
    }
}

public sealed class UserDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

public abstract record SampleError : Error;
public sealed record UserNotFound(int Id, string? Reason = null) : SampleError;
public sealed record InvalidUserId(int Id) : SampleError;
