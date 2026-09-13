using FlowOnRails.Extensions.Linq;

namespace FlowOnRails.Tests.Extensions;

public class LinqExtensionsTests
{
    private record TestError(string Message) : Error;

    [Fact]
    public void Combine_AllSuccess_ReturnsSuccessWithAllValues()
    {
        var results = new[] { RailFor<int>.Ok(1), RailFor<int>.Ok(2), RailFor<int>.Ok(3) };

        var combined = results.Combine();

        Assert.True(combined.IsSuccess);
        Assert.Equal([1, 2, 3], combined.Value);
    }

    [Fact]
    public void Combine_ContainsFailure_ReturnsFirstFailure()
    {
        var error = new TestError("Failure");
        var results = new[]
        {
            RailFor<int>.Ok(1), RailFor<int>.Fail(error), RailFor<int>.Fail(new TestError("Second failure"))
        };

        var combined = results.Combine();

        Assert.True(combined.IsFailure);
        Assert.Equal(error, combined.Error);
    }

    [Fact]
    public void SelectOks_FiltersSuccessfulValues()
    {
        var results = new[] { RailFor<int>.Ok(1), RailFor<int>.Fail(new TestError("Error")), RailFor<int>.Ok(3) };

        var oks = results.SelectOks().ToList();

        Assert.Equal(2, oks.Count);
        Assert.Equal(1, oks[0]);
        Assert.Equal(3, oks[1]);
    }

    [Fact]
    public void SelectErrors_FiltersFailedErrors()
    {
        var error1 = new TestError("Error1");
        var error2 = new TestError("Error2");
        var results = new[] { RailFor<int>.Ok(1), RailFor<int>.Fail(error1), RailFor<int>.Fail(error2) };

        var errors = results.SelectErrors().ToList();

        Assert.Equal(2, errors.Count);
        Assert.Equal(error1, errors[0]);
        Assert.Equal(error2, errors[1]);
    }

    [Fact]
    public void Partition_SeparatesSuccessesAndFailures()
    {
        var error = new TestError("Error");
        var results = new[] { RailFor<int>.Ok(1), RailFor<int>.Fail(error), RailFor<int>.Ok(3) };

        var (oks, errors) = results.Partition();

        Assert.Equal([1, 3], oks);
        Assert.Single(errors);
        Assert.Equal(error, errors[0]);
    }

    [Fact]
    public void Map_TransformsSuccessfulValues()
    {
        var results = new[] { RailFor<int>.Ok(1), RailFor<int>.Fail(new TestError("Error")), RailFor<int>.Ok(3) };

        var mapped = results.Map(x => x.ToString()).ToList();

        Assert.Equal(3, mapped.Count);
        Assert.True(mapped[0].IsSuccess);
        Assert.Equal("1", mapped[0].Value);
        Assert.True(mapped[1].IsFailure);
        Assert.True(mapped[2].IsSuccess);
        Assert.Equal("3", mapped[2].Value);
    }

    [Fact]
    public void Bind_BindsSuccessfulValues()
    {
        var results = new[] { RailFor<int>.Ok(1), RailFor<int>.Fail(new TestError("Error")), RailFor<int>.Ok(3) };

        var bound = results.Bind(x => RailFor<string>.Ok(x.ToString())).ToList();

        Assert.Equal(3, bound.Count);
        Assert.True(bound[0].IsSuccess);
        Assert.Equal("1", bound[0].Value);
        Assert.True(bound[1].IsFailure);
        Assert.True(bound[2].IsSuccess);
        Assert.Equal("3", bound[2].Value);
    }
}
