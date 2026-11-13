namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    [Theory]
    [InlineData("RailFor<Rail> wrapped = Rail.Fail(new A());")]
    [InlineData("RailFor<RailFor<int>> wrapped = Rail.Fail<int>(new A());")]
    [InlineData("RailFor<Rail> wrapped = default(Rail);")]
    [InlineData("var wrapped = Rail.Ok(Rail.Fail(new A()));")]
    [InlineData("var wrapped = Wrap<Rail>(Rail.Fail(new A()));")]
    public async Task RailUsedAsSuccessValue_DoesNotReuseThePayloadState(string initialization)
    {
        var diagnostic = Assert.Single(await Analyze($$"""
            {{initialization}}

            return wrapped.IsSuccess ? new B() : new C();
            """, declarations: """
            private static RailFor<TValue> Wrap<TValue>(TValue value) where TValue : notnull => value;
            """));

        Assert.Equal("B", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Equal("False", diagnostic.Properties["HasUnresolvedConditions"]);
    }

    [Theory]
    [InlineData("Rail result = Rail.Fail<int>(new A()); return result;", "Rail")]
    [InlineData("RailFor<int> result = new A(); return result;", "RailFor<int>")]
    [InlineData("return Rail.Ok(1).Bind(value => Rail.Fail<int>(new A()));", "RailFor<int>")]
    public async Task StatePreservingConversionsAndFailures_RetainTheirErrors(string body, string returnType)
    {
        var diagnostic = Assert.Single(await Analyze(body, returnType: returnType));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Theory]
    [InlineData("_ = new Invoke(Change);")]
    [InlineData("Action callback = Change; _ = new Invoke(callback);")]
    [InlineData("Func<int> callback = () => { Change(); return 1; }; _ = new InvokeValue(callback);")]
    public async Task ConstructorCallbacks_DoNotPreserveAStaleCapturedValue(string construction)
    {
        var diagnostic = Assert.Single(await Analyze($$"""
            RailFor<int> result = new A();
            void Change() => result = new B();
            {{construction}}

            return result;
            """, declarations: """
            private sealed class Invoke
            {
                public Invoke(Action action) => action();
            }
            private sealed class InvokeValue
            {
                public InvokeValue(Func<int> action) => action();
            }
            """));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("effects", diagnostic.Properties["Limitations"]);
    }

    [Theory]
    [InlineData("var ignored = Trigger;")]
    [InlineData("_ = new InvokeStored();")]
    public async Task EscapedLocalMethodGroup_RegistersPossibleCapturedEffects(string trigger)
    {
        var diagnostic = Assert.Single(await Analyze($$"""
            RailFor<int> result = new A();
            void Change() => result = new B();
            storedCallback = Change;
            result = new A();
            {{trigger}}

            return result;
            """, declarations: """
            private static Action? storedCallback;
            private static int Trigger { get { storedCallback!(); return 0; } }
            private sealed class InvokeStored
            {
                public InvokeStored() => storedCallback!();
            }
            """));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("effects", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task GenericConstructor_InvalidatesValuesCapturedByAnEscapedMethodGroup()
    {
        var diagnostic = Assert.Single(await Analyze("return Run<InvokeStored>();", declarations: """
            private static Action? storedCallback;
            private static RailFor<int> Run<TInvoke>() where TInvoke : new()
            {
                RailFor<int> result = new A();
                void Change() => result = new B();
                storedCallback = Change;
                result = new A();
                _ = new TInvoke();

                return result;
            }
            private sealed class InvokeStored
            {
                public InvokeStored() => storedCallback!();
            }
            """));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("effects", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task UnusedLocalMethodGroup_DoesNotExecuteItsBody()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> result = new A();
            void Change() => result = new B();
            Action unused = Change;

            return result;
            """));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }
}
