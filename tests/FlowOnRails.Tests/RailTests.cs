namespace FlowOnRails.Tests;

public class ResultTests
{
    private record TestError(string Message) : Error;

    [Fact]
    public void Ok_ReturnsSuccessfulRail()
    {
        var rail = Rail.Ok();

        Assert.True(rail.IsSuccess);
        Assert.False(rail.IsFailure);
        Assert.Null(rail.Error);
    }

    [Fact]
    public void Fail_ReturnsFailedResultWithError()
    {
        var error = new TestError("Failure");
        var rail = Rail.Fail(error);

        Assert.False(rail.IsSuccess);
        Assert.True(rail.IsFailure);
        Assert.Equal(error, rail.Error);
    }

    [Fact]
    public void ImplicitConversionFromError_ReturnsFailedRail()
    {
        var error = new TestError("Implicit error");
        Rail rail = error;

        Assert.True(rail.IsFailure);
        Assert.Equal(error, rail.Error);
    }

    [Fact]
    public async Task OkAsync_ReturnsSuccessfulResultTask()
    {
        var rail = await Rail.OkAsync();

        Assert.True(rail.IsSuccess);
        Assert.Null(rail.Error);
    }

    [Fact]
    public async Task FailAsync_ReturnsFailedResultTask()
    {
        var error = new TestError("Async error");
        var rail = await Rail.FailAsync(error);

        Assert.True(rail.IsFailure);
        Assert.Equal(error, rail.Error);
    }
}
