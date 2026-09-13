namespace FlowOnRails.Tests;

public class ResultOfTTests
{
    private record TestError(string Message) : Error;

    [Fact]
    public void Ok_ReturnsSuccessfulResultWithValue()
    {
        var value = "Success Value";
        var rail = RailFor<string>.Ok(value);

        Assert.True(rail.IsSuccess);
        Assert.False(rail.IsFailure);
        Assert.Null(rail.Error);
        Assert.Equal(value, rail.Value);
    }

    [Fact]
    public void Ok_WithNullValue_ThrowsArgumentNullException()
    {
#nullable disable
        Assert.Throws<ArgumentNullException>(() => RailFor<string>.Ok(null));
#nullable enable
    }

    [Fact]
    public void Fail_ReturnsFailedResultWithError()
    {
        var error = new TestError("Failure");
        var rail = RailFor<string>.Fail(error);

        Assert.False(rail.IsSuccess);
        Assert.True(rail.IsFailure);
        Assert.Equal(error, rail.Error);
    }

    [Fact]
    public void ImplicitConversionFromValue_ReturnsSuccessfulRail()
    {
        var value = "Implicit Value";
        RailFor<string> rail = value;

        Assert.True(rail.IsSuccess);
        Assert.Equal(value, rail.Value);
    }

    [Fact]
    public void ImplicitConversionFromError_ReturnsFailedRail()
    {
        var error = new TestError("Implicit Error");
        RailFor<string> rail = error;

        Assert.True(rail.IsFailure);
        Assert.Equal(error, rail.Error);
    }

    [Fact]
    public void Result_OkGeneric_CreatesSuccessfulResultOfT()
    {
        var rail = Rail.Ok("Value");

        Assert.True(rail.IsSuccess);
        Assert.Equal("Value", rail.Value);
    }

    [Fact]
    public void Result_FailGeneric_CreatesFailedResultOfT()
    {
        var error = new TestError("Failure");
        var rail = Rail.Fail<string>(error);

        Assert.True(rail.IsFailure);
        Assert.Equal(error, rail.Error);
    }

    [Fact]
    public async Task Result_OkAsyncGeneric_ReturnsSuccessfulResultTask()
    {
        var rail = await Rail.OkAsync("Value");

        Assert.True(rail.IsSuccess);
        Assert.Equal("Value", rail.Value);
    }

    [Fact]
    public async Task Result_FailAsyncGeneric_ReturnsFailedResultTask()
    {
        var error = new TestError("Failure");
        var rail = await Rail.FailAsync<string>(error);

        Assert.True(rail.IsFailure);
        Assert.Equal(error, rail.Error);
    }

    [Fact]
    public void ConversionToNonGenericResult_Success()
    {
        var resultOfT = RailFor<string>.Ok("Value");
        Rail rail = resultOfT;

        Assert.True(rail.IsSuccess);
    }

    [Fact]
    public void ConversionToNonGenericResult_Failure()
    {
        var error = new TestError("Failure");
        var resultOfT = RailFor<string>.Fail(error);
        Rail rail = resultOfT;

        Assert.True(rail.IsFailure);
        Assert.Equal(error, rail.Error);
    }
}
