using FlowOnRails.Extensions.Tasks;

namespace FlowOnRails.Tests.Extensions;

public class TaskExtensionsTests
{
    private record TestError(string Message) : Error;

    [Fact]
    public async Task MatchAsync_OnSuccess_ReturnsSuccessValue()
    {
        var task = Task.FromResult(Rail.Ok());
        var matchRail = await task.MatchAsync(() => 1, _ => 0);

        Assert.Equal(1, matchRail);
    }

    [Fact]
    public async Task MatchAsync_OnFailure_ReturnsFailureValue()
    {
        var task = Task.FromResult(Rail.Fail(new TestError("Error")));
        var matchRail = await task.MatchAsync(() => 1, _ => 0);

        Assert.Equal(0, matchRail);
    }

    [Fact]
    public async Task BindAsync_OnSuccess_BindsNextTask()
    {
        var task = Task.FromResult(Rail.Ok());
        var nextTask = Task.FromResult(Rail.Ok());

        var boundRail = await task.BindAsync(() => nextTask);

        Assert.True(boundRail.IsSuccess);
    }

    [Fact]
    public async Task TapAsync_OnSuccess_ExecutesAction()
    {
        var task = Task.FromResult(Rail.Ok());
        var called = false;

        var rail = await task.TapAsync(() =>
        {
            called = true;
            return Task.CompletedTask;
        });

        Assert.True(rail.IsSuccess);
        Assert.True(called);
    }

    [Fact]
    public async Task EnsureAsync_OnSuccessAndPredicateTrue_ReturnsSuccess()
    {
        var task = Task.FromResult(Rail.Ok());
        var rail = await task.EnsureAsync(() => true, new TestError("Error"));

        Assert.True(rail.IsSuccess);
    }
}
