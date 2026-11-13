namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    public static TheoryData<string, string> KnownCallableCases => new()
    {
        { "RailFor<int> Read() => new A(); return Read();", "A" },
        { "RailFor<int> Forward(Error error) => error; return Forward(new B());", "B" },
        { "Func<RailFor<int>> read = () => new A(); return read();", "A" },
        { "Func<RailFor<int>> read = Concrete; return read();", "B" },
        { "RailFor<int> Read() => new A(); Func<RailFor<int>> read = Read; return read.Invoke();", "A" },
        { "Func<RailFor<int>> read = flag ? () => new A() : () => new B(); return read();", "A, B" },
        { "Func<RailFor<int>> read = () => new A(); read = () => new B(); return read();", "B" },
        { "RailFor<int> result = new A(); void Change() => result = new B(); Change(); return result;", "B" },
        { "RailFor<int> result = new A(); Action change = () => result = new B(); change(); return result;", "B" },
        { "RailFor<int> result = new A(); Action change = flag ? () => result = new B() : () => result = new C(); change(); return result;", "B, C" },
        { "RailFor<int> result = new A(); Rail.Ok().Tap(() => result = new B()); return result;", "B" },
        { "RailFor<int> result = new A(); RailFor<int> input = flag ? Rail.Ok(1) : new C(); input.Tap(value => result = new B()); return result;", "A, B" },
        { "Func<Error, Error> transform = error => error is A ? new B() : error; return Rail.Fail<int>(new A()).MapError(transform);", "B" },
        { "Error error = new A(); Func<Error> read = () => error; error = new B(); return read();", "B" },
        { "RailFor<int> result = new A(); void Outer() { void Inner() => result = new B(); Inner(); } Outer(); return result;", "B" },
        { "RailFor<int> result = new A(); void Change() { try { result = new B(); } finally { result = new C(); } } Change(); return result;", "C" }
    };

    [Theory]
    [MemberData(nameof(KnownCallableCases))]
    public async Task KnownCallables_PropagateTargetsAndNormalEffects(string body, string expectedErrors)
    {
        var diagnostic = Assert.Single(await Analyze(body));

        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Theory]
    [InlineData("RailFor<int> result = new A(); Mutate(ref result); return result;", "B")]
    [InlineData("Replace(out var result); return result;", "C")]
    [InlineData("RailFor<int> result = new A(); ref var alias = ref result; alias = new B(); return result;", "B")]
    [InlineData("RailFor<int> first = new A(), second = new B(); ref var alias = ref first; alias = ref second; alias = new C(); return first;", "A")]
    [InlineData("RailFor<int> first = new A(), second = new B(); ref var alias = ref first; alias = ref second; alias = new C(); return second;", "C")]
    [InlineData("RailFor<int> result = new A(); Twice(ref result, ref result); return result;", "C")]
    public async Task ReferenceCells_PropagateWritesThroughAliases(string body, string expectedErrors)
    {
        var diagnostic = Assert.Single(await Analyze(body, declarations: """
            private static void Replace(out RailFor<int> result) => result = new C();
            private static void Twice(ref RailFor<int> first, ref RailFor<int> second)
            {
                first = new B();
                second = new C();
            }
            """));

        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Theory]
    [InlineData("private static RailFor<int> Read => new A();", "A")]
    [InlineData("private static RailFor<int> Read { get { return new B(); } }", "B")]
    public async Task ConcreteGetters_AnalyzeAvailableBodies(string declaration, string expectedErrors)
    {
        var diagnostic = Assert.Single(await Analyze("return Read;", declarations: declaration));

        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task UnknownDelegate_AlwaysRetainsAnOpenAlternative()
    {
        var diagnostic = Assert.Single(await Analyze("Func<RailFor<int>> read = flag ? unknown : () => new A(); return read();",
            parameters: "bool flag, Func<RailFor<int>> unknown"));

        Assert.Contains("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("delegate target", diagnostic.Properties["Limitations"]);
    }

    [Theory]
    [InlineData("return First(flag);", "A, B")]
    [InlineData("return First(true);", "A")]
    public async Task MutualRecursion_ConvergesAcrossAbstractCallContexts(string body, string expectedErrors)
    {
        var diagnostic = Assert.Single(await Analyze(body, declarations: """
            private static RailFor<int> First(bool stop) => stop ? new A() : Second(stop);
            private static RailFor<int> Second(bool stop) => stop ? new B() : First(stop);
            """));

        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task RecursiveReferenceEffects_StayOpenUntilTheirStateSummaryIsSupported()
    {
        var diagnostic = Assert.Single(await Analyze("RailFor<int> result = new A(); Change(ref result, flag); return result;",
            declarations: """
            private static void Change(ref RailFor<int> result, bool stop)
            {
                if (stop) { result = new B(); return; }
                Change(ref result, stop);
            }
            """));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("Recursive captured or reference effects", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task DelegateArguments_InvokeTheirKnownTargetsInAnotherBody()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> result = new A();
            Invoke(() => result = new B());
            return result;
            """, declarations: "private static void Invoke(Action action) => action();"));

        Assert.Equal("B", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task ConditionalReferenceStorage_NeverKeepsAnUnchangedClosedAnswer()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> first = new A(), second = new B();
            ref var alias = ref (flag ? ref first : ref second);
            alias = new C();
            return first;
            """));

        Assert.True(diagnostic.Properties["HasUnknownErrors"] == "True"
            || diagnostic.Properties["ErrorTypes"]!.Contains("C"));
    }

    [Fact]
    public async Task UnknownReferenceTargetPassedToACall_InvalidatesPotentialCells()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> first = new A(), second = new C();
            ref var alias = ref (flag ? ref first : ref second);
            Mutate(ref alias);
            return first;
            """));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task InterfaceCallWithUnknownReferenceStorage_InvalidatesPotentialCells()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> first = new A(), second = new C();
            ref var alias = ref (flag ? ref first : ref second);
            changer.Change(ref alias);
            return first;
            """, parameters: "bool flag, IChanger changer", declarations:
            "public interface IChanger { void Change(ref RailFor<int> result); }"));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task RefReassignmentAcrossBranches_UsesWeakWritesForAmbiguousCells()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> first = new A(), second = new B();
            ref var alias = ref first;
            if (flag) alias = ref second;
            alias = new C();
            return first;
            """));

        Assert.Equal("A, C", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task IgnoredFaultedAsyncCall_DoesNotKillTheSynchronousContinuation()
    {
        var diagnostic = Assert.Single(await Analyze("_ = Fault(); return new A();", declarations: """
            private static async Task<RailFor<int>> Fault()
            {
                await Task.Yield();
                throw new Exception();
            }
            """));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task AsyncCapturedReads_DoNotAssumeTheCreationTimeValue()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> result = new A();
            async Task<RailFor<int>> Read() { await Task.Yield(); return result; }
            var pending = Read();
            result = new B();
            return await pending;
            """, returnType: "Task<RailFor<int>>", isAsync: true));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("Asynchronous captured", diagnostic.Properties["Limitations"]);
    }
}
