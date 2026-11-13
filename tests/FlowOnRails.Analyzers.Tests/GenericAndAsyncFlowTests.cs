namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    [Theory]
    [InlineData("return Forward<int>(new A());", "A")]
    [InlineData("return Create<string>();", "Study.Failure<string>")]
    [InlineData("return Container<int>.Create();", "Study.Failure<int>")]
    [InlineData("return Outer<int>();", "Study.Failure<int>")]
    public async Task GenericBodies_SubstituteMethodAndContainingTypeArguments(string body, string expectedErrors)
    {
        var diagnostic = Assert.Single(await Analyze(body, declarations: """
            private sealed record Failure<TValue> : Error;
            private static RailFor<TValue> Forward<TValue>(RailFor<TValue> result) where TValue : notnull => result;
            private static RailFor<int> Create<TValue>() => new Failure<TValue>();
            private static RailFor<int> Outer<TValue>() => Create<TValue>();
            private static class Container<TValue>
            {
                public static RailFor<int> Create() => new Failure<TValue>();
            }
            """));

        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Theory]
    [InlineData("Task<RailFor<int>>", "return Task.FromResult(Rail.Fail<int>(new A()));", false)]
    [InlineData("ValueTask<RailFor<int>>", "return ValueTask.FromResult(Rail.Fail<int>(new A()));", false)]
    [InlineData("ValueTask<RailFor<int>>", "return new ValueTask<RailFor<int>>(Rail.Fail<int>(new A()));", false)]
    [InlineData("Task<RailFor<int>>", "await Task.Yield(); return new A();", true)]
    [InlineData("ValueTask<RailFor<int>>", "await Task.Yield(); return new A();", true)]
    [InlineData("Task<RailFor<int>>", "return await Task.FromResult(Rail.Fail<int>(new A())).ConfigureAwait(false);", true)]
    public async Task AsyncMethods_TrackTheNormalAwaitedResult(string returnType, string body, bool isAsync)
    {
        var diagnostic = Assert.Single(await Analyze(body, returnType: returnType, isAsync: isAsync));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task AsyncCalls_PropagateThroughGenericHelpers()
    {
        var diagnostic = Assert.Single(await Analyze("return await Forward<int>(Fetch());",
            returnType: "Task<RailFor<int>>", isAsync: true, declarations: """
            private static Task<RailFor<int>> Fetch() => Task.FromResult(Rail.Fail<int>(new B()));
            private static async Task<RailFor<TValue>> Forward<TValue>(Task<RailFor<TValue>> source)
                where TValue : notnull => await source.ConfigureAwait(false);
            """));

        Assert.Equal("B", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Theory]
    [InlineData("return Task.FromResult(Rail.Fail<int>(new A())).BindAsync(value => Task.FromResult(Rail.Fail<int>(new B())));", "A")]
    [InlineData("return Task.FromResult(Rail.Ok(1)).BindAsync(value => Task.FromResult(Rail.Fail<int>(new B())));", "B")]
    [InlineData("return Task.FromResult(Rail.Ok(1)).BindAsync(async value => { await Task.Yield(); return Rail.Fail<int>(new B()); });", "B")]
    [InlineData("return Task.FromResult(Rail.Fail<int>(new A())).MapErrorAsync(error => Task.FromResult<Error>(new B()));", "B")]
    [InlineData("return Task.FromResult(Rail.Fail<int>(new A())).RecoverWithAsync(error => Task.FromResult(Rail.Ok(1)));", "")]
    [InlineData("return ValueTask.FromResult(Rail.Ok(1)).BindAsync(value => ValueTask.FromResult(Rail.Fail<int>(new B()))).AsTask();", "B")]
    public async Task AsyncOperators_ApplyCallbacksOnlyOnTheirApplicablePaths(string body, string expectedErrors)
    {
        var diagnostic = Assert.Single(await Analyze(body, returnType: "Task<RailFor<int>>"));

        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task FaultedTask_DoesNotIntroduceAnErrorFromAnUnreachableNormalReturn()
    {
        var diagnostic = Assert.Single(await Analyze("""
            await Task.FromException<RailFor<int>>(new InvalidOperationException());
            return new A();
            """, returnType: "Task<RailFor<int>>", isAsync: true));

        Assert.Equal("", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["MaySucceed"]);
    }

    [Fact]
    public async Task AsyncInterface_KeepsTheDeclaredErrorsAndOpenRemainder()
    {
        var diagnostic = Assert.Single(await Analyze("return await reader.Read();",
            parameters: "IReader reader", returnType: "Task<RailFor<int>>", isAsync: true, declarations: """
            public interface IReader
            {
                [ResultsWith<A, B>]
                ValueTask<RailFor<int>> Read();
            }
            """));

        Assert.Contains("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Contains("B", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
    }
}
