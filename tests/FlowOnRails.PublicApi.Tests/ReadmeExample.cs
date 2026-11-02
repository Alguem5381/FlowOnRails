namespace FlowOnRails.PublicApi.Tests;

internal sealed class ReadmeExample
{
    // README:GetUser:start
    public RailFor<User> GetUser(int id)
    {
        if (id <= 0)
            return Rail.Fail<User>(new ValidationError("ID must be positive."));

        var user = Database.Find(id);
        if (user is null)
            return Rail.Fail<User>(new NotFoundError("User not found."));

        return Rail.Ok(user);
    }
    // README:GetUser:end
}

internal sealed record User(int Id, string Name);
internal sealed record ValidationError(string Message) : Error;
internal sealed record NotFoundError(string Message) : Error;

internal static class Database
{
    public static User? Find(int id) => id == 10 ? new User(id, "Example") : null;
}
