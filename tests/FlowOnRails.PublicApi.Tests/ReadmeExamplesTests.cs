using System.Text.RegularExpressions;

namespace FlowOnRails.PublicApi.Tests;

public class ReadmeExamplesTests
{
    [Fact]
    public void GetUser_DocumentationMatchesCompiledPublicExample()
    {
        var documented = Regex.Match(ReadResource("README.md"),
            @"```csharp\r?\n(?<code>public RailFor<User> GetUser\(int id\).*?)```",
            RegexOptions.Singleline);
        var compiled = Regex.Match(ReadResource("ReadmeExample.cs"),
            @"// README:GetUser:start\r?\n(?<code>.*?)\s*// README:GetUser:end",
            RegexOptions.Singleline);

        Assert.True(documented.Success);
        Assert.True(compiled.Success);
        Assert.Equal(Normalize(compiled.Groups["code"].Value),
            Normalize(documented.Groups["code"].Value));
    }

    [Fact]
    public void GetUser_SuccessSupportsNullSafeUnwrapping()
    {
        var result = new ReadmeExample().GetUser(10);
        if (!result.TryUnwrap(out var user, out var error))
            throw new InvalidOperationException(error.ToString());

        Assert.Equal("Example", user.Name);
        Assert.Equal(10, user.Id);
    }

    [Theory]
    [InlineData(0, typeof(ValidationError))]
    [InlineData(11, typeof(NotFoundError))]
    public void GetUser_FailureExposesTheExpectedDomainError(int id, Type errorType)
    {
        var result = new ReadmeExample().GetUser(id);
        if (result.TryUnwrap(out _, out var error))
            throw new InvalidOperationException("Expected failure.");

        Assert.Equal(errorType, error.GetType());
    }

    private static string ReadResource(string name)
    {
        using var stream = typeof(ReadmeExamplesTests).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource: {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Normalize(string source) =>
        string.Join("\n", source.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0));
}
