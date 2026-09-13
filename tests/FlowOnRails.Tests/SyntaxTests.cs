namespace FlowOnRails.Tests.Syntax;

public class SyntaxTests
{
    private record TestError(string Message) : Error;

    [Fact]
    public void Select_TransformsValue()
    {
        var rail = RailFor<int>.Ok(5);

        var query = from x in rail
            select x * 2;

        Assert.True(query.IsSuccess);
        Assert.Equal(10, query.Value);
    }

    [Fact]
    public void Select_OnFailure_ReturnsFailure()
    {
        var error = new TestError("Failure");
        var rail = RailFor<int>.Fail(error);

        var query = from x in rail
            select x * 2;

        Assert.True(query.IsFailure);
        Assert.Equal(error, query.Error);
    }

    [Fact]
    public void SelectMany_BothSuccess_ReturnsCombinedValue()
    {
        var result1 = RailFor<int>.Ok(5);
        var result2 = RailFor<int>.Ok(10);

        var query = from x in result1
            from y in result2
            select x + y;

        Assert.True(query.IsSuccess);
        Assert.Equal(15, query.Value);
    }

    [Fact]
    public void SelectMany_FirstFails_ReturnsFirstFailure()
    {
        var error1 = new TestError("Failure 1");
        var result1 = RailFor<int>.Fail(error1);
        var result2 = RailFor<int>.Ok(10);

        var query = from x in result1
            from y in result2
            select x + y;

        Assert.True(query.IsFailure);
        Assert.Equal(error1, query.Error);
    }

    [Fact]
    public void SelectMany_SecondFails_ReturnsSecondFailure()
    {
        var result1 = RailFor<int>.Ok(5);
        var error2 = new TestError("Failure 2");
        var result2 = RailFor<int>.Fail(error2);

        var query = from x in result1
            from y in result2
            select x + y;

        Assert.True(query.IsFailure);
        Assert.Equal(error2, query.Error);
    }
}
