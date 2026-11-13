using System.Collections.Immutable;

using FlowOnRails.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Append(typeof(Error).Assembly.Location).Distinct(StringComparer.Ordinal)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    public static TheoryData<string, string, bool> LocalCases => new()
    {
        { "return Rail.Ok(1);", "", false },
        { "return new A();", "A", false },
        { "return Rail.Fail<int>(new A());", "A", false },
        { "var discarded = new A(); return Rail.Ok(1);", "", false },
        { "var result = Rail.Fail<int>(new A()); result = Rail.Ok(1); return result;", "", false },
        { "Error error = new A(); Error copy = error; return Rail.Fail<int>(copy);", "A", false },
        { "RailFor<int> result; if (flag) result = new A(); else result = new B(); return result;", "A, B", true },
        { "return flag ? Rail.Fail<int>(new A()) : Rail.Fail<int>(new B());", "A, B", true },
        { "if (1 + 1 == 2) return new A(); return new B();", "A", false },
        { "if (flag) throw new InvalidOperationException(); return new A();", "A", true },
        { "Error error = new A(); if (error is A) return Rail.Ok(1); return Rail.Fail<int>(error);", "", false },
        { "Error error = flag ? new A() : new B(); if (error is A) return Rail.Ok(1); return Rail.Fail<int>(error);", "B", true },
        { "Error error = flag ? new A() : new B(); return error is A ? Rail.Ok(1) : Rail.Fail<int>(error);", "B", true },
        { "Error error = flag ? new A() : new B(); return error switch { A => Rail.Ok(1), _ => Rail.Fail<int>(error) };", "B", true },
        { "Error error = new A(); if (error is A && flag) return Rail.Ok(1); return Rail.Fail<int>(error);", "A", true },
        { "Error error = new A(); if (error is A { Code: 1 }) return Rail.Ok(1); return Rail.Fail<int>(error);", "A", true },
        { "Error error = flag ? new A() : new B(); if (error is not A) return error; return Rail.Ok(1);", "B", true },
        { "Error error = flag ? new A() : new B(); if (error is A or B) return Rail.Ok(1); return error;", "", true },
        { "Error error = new A(); var copy = (A)error; return copy;", "A", false },
        { "Error error = new A(); var copy = error as A; return copy!;", "A", false },
        { "var result = Rail.Fail<int>(new A()); if (!result.TryUnwrap(out var value, out var error)) return error; return Rail.Ok(value);", "A", false },
        { "var result = Rail.Fail<int>(new A()); var ok = result.TryUnwrap(out var value, out var error); if (!ok) return error!; return Rail.Ok(value);", "A", false },
        { "RailFor<int> result = flag ? new A() : new B(); if (!result.TryUnwrap(out _, out var error) && error is A) return Rail.Ok(1); return result;", "B", true },
        { "var result = Rail.Ok(1); var ok = result.TryUnwrap(out _, out var error); result = Rail.Fail<int>(new B()); if (ok) return result; return Rail.Ok(1);", "B", false },
        { "var result = Rail.Fail<int>(new A()); while (flag) { result = Rail.Fail<int>(new B()); flag = false; } return result;", "A, B", true },
        { "var result = Rail.Fail<int>(new A()); return result.IsFailure ? result : Rail.Ok(1);", "A", false },
        { "Func<RailFor<int>> unused = () => new A(); return Rail.Ok(1);", "", false },
        { "bool recover = true; recover &= flag; Error error = new A(); if (recover) return Rail.Ok(1); return error;", "A", true },
        { "Error error = new A(); if (error is null) return new B(); return error;", "A", false }
    };

    [Theory]
    [MemberData(nameof(LocalCases))]
    public async Task LocalFlow_ReportsOnlyErrorsReachingFailureReturns(string body, string errors, bool unresolved)
    {
        var diagnostic = Assert.Single(await Analyze(body));

        Assert.Equal(errors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal(unresolved.ToString(), diagnostic.Properties["HasUnresolvedConditions"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
    }

    public static TheoryData<string, string, bool> OperatorCases => new()
    {
        { "return Rail.Fail<int>(new A()).Map(value => value + 1);", "A", false },
        { "return Rail.Fail<int>(new A()).Bind(value => Rail.Fail<int>(new B()));", "A", false },
        { "return Rail.Ok(1).Bind(value => Rail.Fail<int>(new B()));", "B", false },
        { "return Rail.Ok(1).Check(value => Rail.Fail(new A()));", "A", false },
        { "return Rail.Ok(1).Check(value => Rail.Fail<string>(new B()));", "B", false },
        { "return Rail.Ok(1).Ensure(value => true, new A());", "", false },
        { "return Rail.Ok(1).Ensure(value => false, new A());", "A", false },
        { "return Rail.Ok(1).Ensure(value => flag, value => new A());", "A", true },
        { "return Rail.Fail<int>(new A()).Ensure(value => flag, new B());", "A", false },
        { "return Rail.Fail<int>(new A()).MapError(error => new B());", "B", false },
        { "return Rail.Fail<int>(new A()).Recover(error => 42);", "", false },
        { "return Rail.Fail<int>(new A()).RecoverWith(error => Rail.Ok(42));", "", false },
        { "return Rail.Fail<int>(new A()).RecoverWith(error => Rail.Fail<int>(new B()));", "B", false },
        { "return Rail.Ok(1).Bind(value => default(RailFor<int>));", "", false },
        { "return Rail.Fail<int>(new A()).RecoverWith(error => default(RailFor<int>));", "", false },
        { "return Rail.Fail<int>(new A()).TapError(error => { Rail.Fail<int>(new B()); });", "A", false },
        { "return Rail.Ok(1).Tap(value => { Rail.Fail<int>(new B()); });", "", false },
        { "return Rail.Ok(1).TapIf(false, value => { Rail.Fail<int>(new B()); });", "", false },
        { "return Rail.Fail<int>(new A()).TapIf(value => flag, value => { Rail.Fail<int>(new B()); });", "A", false },
        { "return Rail.Fail<int>(new A()).Match(value => Rail.Ok(value), error => Rail.Fail<int>(new B()));", "B", false },
        { "RailFor<int> result = flag ? new A() : new B(); return result.RecoverWith(error => error is A ? Rail.Ok(1) : Rail.Fail<int>(error));", "B", true },
        { "RailFor<int> result = flag ? new A() : new B(); return result.MapError(error => error is A ? new C() : error);", "B, C", true }
    };

    [Theory]
    [MemberData(nameof(OperatorCases))]
    public async Task Operators_RespectTheSuccessfulAndFailedPaths(string body, string errors, bool unresolved)
    {
        var diagnostic = Assert.Single(await Analyze(body));

        Assert.Equal(errors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal(unresolved.ToString(), diagnostic.Properties["HasUnresolvedConditions"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task DisabledByDefault_ProducesNoDiagnostics()
    {
        Assert.Empty(await Analyze("return new A();", enabled: false));
    }

    [Fact]
    public async Task ConcreteCalledMethod_PropagatesItsAvailableBody()
    {
        var diagnostic = Assert.Single(await Analyze("return Concrete();"));

        Assert.Equal("B", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Equal("", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task UnavailableCalledMethod_RemainsOpen()
    {
        var diagnostic = Assert.Single(await Analyze("return External();"));

        Assert.Equal("FlowOnRails.Error (+ subtypes)", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Equal("False", diagnostic.Properties["HasUnresolvedConditions"]);
        Assert.Contains("unavailable", diagnostic.Properties["Limitations"]);
    }

    [Theory]
    [InlineData("return Forward(new A());", "A")]
    [InlineData("return Forward(flag ? new A() : new B());", "A, B")]
    [InlineData("var first = Forward(new A()); var second = Forward(new B()); return first;", "A")]
    [InlineData("Forward(new B()); return new A();", "A")]
    public async Task ConcreteCalls_UseArgumentAlternativesWithoutMixingCallContexts(
        string body,
        string expectedErrors)
    {
        var diagnostic = Assert.Single(await Analyze(body, declarations: """
            private static RailFor<int> Forward(RailFor<int> input) => input;
            """));

        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task ConcreteErrorReturningMethod_PropagatesTheErrorUsedByItsCaller()
    {
        var diagnostic = Assert.Single(await Analyze(
            "return Rail.Fail<int>(Keep(new A()));",
            declarations: "private static Error Keep(Error error) => error;"));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task ConcreteCall_UsesAKnownBooleanArgumentToSelectTheReturnedErrors()
    {
        var diagnostic = Assert.Single(await Analyze("return Choose(true);", declarations: """
            private static RailFor<int> Choose(bool first) => first ? new A() : new B();
            """));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Equal("False", diagnostic.Properties["HasUnresolvedConditions"]);
    }

    [Fact]
    public async Task ConcreteCalls_PropagateAcrossSeveralMethodBodies()
    {
        var diagnostic = Assert.Single(await Analyze("return Outer();", declarations: """
            private static RailFor<int> Outer() => Middle();
            private static RailFor<int> Middle() => Rail.Fail<int>(CreateError());
            private static Error CreateError() => new C();
            """));

        Assert.Equal("C", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task CalledMethodWithoutNormalReturns_DoesNotIntroduceAnErrorReturn()
    {
        var diagnostic = Assert.Single(await Analyze("""
            if (flag)
                return Stop();

            return new A();
            """, declarations: """
            private static RailFor<int> Stop() => throw new InvalidOperationException();
            """));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task RecursiveCalls_ConvergeOnErrorsFromNormalReturns()
    {
        var diagnostic = Assert.Single(await Analyze("return Recur(flag);", declarations: """
            private static RailFor<int> Recur(bool stop) => stop ? new A() : Recur(stop);
            """));

        Assert.Contains("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task ExcessiveCallDepth_ProducesAnOpenSummary()
    {
        var declarations = string.Join(Environment.NewLine, Enumerable.Range(0, 40).Select(methodIndex =>
            $"private static RailFor<int> Chain{methodIndex}() => Chain{methodIndex + 1}();"));
        declarations += "private static RailFor<int> Chain40() => new B();";
        var diagnostic = Assert.Single(await Analyze("return Chain0();", declarations: declarations));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("could not be fully analyzed", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task GenericCalledMethod_SubstitutesTheConstructedErrorType()
    {
        var diagnostic = Assert.Single(await Analyze("return Create<B>();", declarations: """
            private static RailFor<int> Create<TError>() where TError : Error, new() => new TError();
            """));

        Assert.Equal("B", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task InterfaceCall_AddsDeclaredErrorsWithoutClosingTheContract()
    {
        var diagnostic = Assert.Single(await Analyze("return reader.Read();", parameters: "IReader reader",
            declarations: """
            public interface IReader
            {
                [ResultsWith<A, B>]
                [ResultsWith<C>]
                RailFor<int> Read();
            }
            """));

        Assert.Equal("A, B, C, FlowOnRails.Error (+ subtypes)", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("dispatch", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task InterfaceCall_RemainsOpenEvenWhenAConcreteImplementationIsVisible()
    {
        var diagnostic = Assert.Single(await Analyze("IReader reader = new Reader(); return reader.Read();",
            declarations: """
            public interface IReader
            {
                [ResultsWith<A>]
                RailFor<int> Read();
            }
            private sealed class Reader : IReader
            {
                public RailFor<int> Read() => new B();
            }
            """));

        Assert.Contains("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task InterfaceCall_CanBeClosedByReplacingItsErrorsInTheCaller()
    {
        var diagnostic = Assert.Single(await Analyze("return reader.Read().MapError(error => new C());",
            parameters: "IReader reader", declarations: """
            public interface IReader
            {
                [ResultsWith<A, B>]
                RailFor<int> Read();
            }
            """));

        Assert.Equal("C", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task InterfaceContractFromReferenceMetadata_PreservesDeclaredTypesAndItsOpenRemainder()
    {
        const string contractSource = """
            using FlowOnRails;
            public sealed record RemoteFailure : Error;
            public interface IRemoteReader
            {
                [ResultsWith<RemoteFailure>]
                RailFor<int> Read();
            }
            """;
        var contractTree = CSharpSyntaxTree.ParseText(contractSource,
            new CSharpParseOptions(LanguageVersion.CSharp14));
        var contractCompilation = CSharpCompilation.Create("ExternalContracts", [contractTree], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var contractStream = new MemoryStream();
        var emission = contractCompilation.Emit(contractStream,
            options: new Microsoft.CodeAnalysis.Emit.EmitOptions(metadataOnly: true, includePrivateMembers: false));
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));

        const string consumerSource = """
            using FlowOnRails;
            public static class Consumer
            {
                public static RailFor<int> Subject(IRemoteReader reader) => reader.Read();
            }
            """;
        var diagnostic = Assert.Single(await AnalyzeSource(consumerSource, true, false, "Subject",
            [MetadataReference.CreateFromImage(contractStream.ToArray())]));

        Assert.Contains("RemoteFailure", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Theory]
    [InlineData("RailFor<int> Read();")]
    [InlineData("RailFor<int> Read() => new B();")]
    public async Task InterfaceDeclaration_ReportsAnOpenContractIncludingDefaultImplementations(string member)
    {
        var source = $$"""
            using FlowOnRails;
            public sealed record A : Error;
            public sealed record B : Error;
            public interface IReader
            {
                [ResultsWith<A>]
                {{member}}
            }
            """;
        var diagnostic = Assert.Single(await AnalyzeSource(source, true, false, "Read"));

        Assert.Contains("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task VirtualCall_RemainsOpenUnlessDispatchCannotBeOverridden()
    {
        const string declarations = """
            public class Reader
            {
                [ResultsWith<A>]
                public virtual RailFor<int> Read() => new A();
            }
            public sealed class ConcreteReader : Reader
            {
                public override RailFor<int> Read() => new B();
            }
            """;
        var openDiagnostic = Assert.Single(await Analyze("return reader.Read();", parameters: "Reader reader",
            declarations: declarations));
        var closedDiagnostic = Assert.Single(await Analyze("return reader.Read();",
            parameters: "ConcreteReader reader", declarations: declarations));

        Assert.Equal("True", openDiagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("A", openDiagnostic.Properties["ErrorTypes"]);
        Assert.Equal("B", closedDiagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", closedDiagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task UnknownCondition_RetainsBothKnownErrorsWithoutOpeningTheirTypes()
    {
        var diagnostic = Assert.Single(await Analyze("if (Environment.TickCount > 0) return new A(); return new B();"));

        Assert.Equal("A, B", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["HasUnresolvedConditions"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("the list of possible errors may be broader than necessary", diagnostic.GetMessage());
    }

    [Fact]
    public async Task UninitializedRail_IsReportedSeparatelyFromDomainFailures()
    {
        var diagnostic = Assert.Single(await Analyze("return default;"));

        Assert.Equal("", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["MayBeInvalid"]);
        Assert.Equal("False", diagnostic.Properties["MaySucceed"]);
    }

    [Fact]
    public async Task ExactConstructionOfNonSealedError_DoesNotIncludeItsDescendants()
    {
        var diagnostic = Assert.Single(await Analyze("return new Parent();"));

        Assert.Equal("Parent", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task ErrorParameter_IsAFamilyUntilItIsNarrowed()
    {
        var diagnostic = Assert.Single(await Analyze("if (incoming is A exact) return exact; return Rail.Ok(1);",
            parameters: "Parent incoming"));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task UnknownResult_CanBeNarrowedToOneKnownError()
    {
        var diagnostic = Assert.Single(await Analyze("""
            var result = External();
            if (!result.TryUnwrap(out _, out var error) && error is A)
                return Rail.Fail<int>(error);
            return Rail.Ok(1);
            """));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Theory]
    [InlineData("new A()", "converted is null", "A")]
    [InlineData("flag ? new A() : new B()", "converted is null", "A, B")]
    [InlineData("new A()", "converted is not B", "A")]
    public async Task TryCast_DoesNotUseCastFailureToEraseTheOriginalRailError(
        string initialResult,
        string condition,
        string expectedErrors)
    {
        var diagnostic = Assert.Single(await Analyze($$"""
            RailFor<int> result = {{initialResult}};
            result.TryUnwrap(out _, out var error);
            var converted = error as B;

            if ({{condition}})
                return result;

            return Rail.Ok(1);
            """));

        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task TryCast_KnownCompatibleErrorDoesNotIntroduceANullPath()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> result = new A();
            result.TryUnwrap(out _, out var error);
            var converted = error as A;

            if (converted is null)
                return new B();

            return Rail.Ok(1);
            """));

        Assert.Equal("", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task TryCast_UnsupportedTargetDoesNotInventANonNullProof()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> result = new A();
            result.TryUnwrap(out _, out var error);
            var converted = (object)error! as IDisposable;

            if (converted is null)
                return result;

            return Rail.Ok(1);
            """));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["HasUnresolvedConditions"]);
        Assert.Contains("Try-casts", diagnostic.Properties["Limitations"]);
    }

    [Theory]
    [InlineData("Error", "result.TryUnwrap(out var value, out _);", "FlowOnRails.Error (+ subtypes)", true)]
    [InlineData("Error", "if (!result.TryUnwrap(out var value, out _)) return Rail.Ok(1);", "FlowOnRails.Error (+ subtypes)", true)]
    [InlineData("Error", "result.TryUnwrap(error: out _, value: out var value);", "FlowOnRails.Error (+ subtypes)", true)]
    [InlineData("Parent", "result.TryUnwrap(out var value, out _);", "Parent (+ subtypes)", true)]
    [InlineData("A", "result.TryUnwrap(out var value, out _);", "A", false)]
    public async Task TryUnwrap_DistinguishesAnErrorSuccessValueFromTheFailureOutput(
        string successType,
        string extraction,
        string expectedErrors,
        bool hasUnknownErrors)
    {
        var diagnostic = Assert.Single(await Analyze($$"""
            var result = Rail.Ok<{{successType}}>(new A());
            {{extraction}}

            return Rail.Fail<int>(value!);
            """));

        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal(hasUnknownErrors.ToString(), diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task TryUnwrap_ErrorSuccessTypeDoesNotPreventNarrowingTheFailureOutput()
    {
        var diagnostic = Assert.Single(await Analyze("""
            var result = Rail.Fail<Error>(new A());

            if (!result.TryUnwrap(out _, out var error))
                return Rail.Fail<int>(error);

            return Rail.Ok(1);
            """));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task UnknownErrors_StayOpenWhenOnlyOneTypeIsRecovered()
    {
        var diagnostic = Assert.Single(await Analyze("""
            var result = External();
            if (!result.TryUnwrap(out _, out var error))
            {
                if (error is A) return Rail.Ok(1);
                return Rail.Fail<int>(error);
            }
            return Rail.Ok(1);
            """));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("except A", diagnostic.Properties["ErrorTypes"]);
    }

    [Fact]
    public async Task EncapsulatingAllUnknownErrors_ClosesTheReturnedFailureTypes()
    {
        var diagnostic = Assert.Single(await Analyze("return External().MapError(error => new C());"));

        Assert.Equal("C", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Theory]
    [InlineData("return Rail.Ok<Error>(new A());")]
    [InlineData("return Rail.Ok(1).Map<Error>(value => new A());")]
    [InlineData("return Rail.Fail<Error>(new B()).Recover(error => new A());")]
    public async Task ErrorUsedAsSuccessValue_IsNotAFailure(string body)
    {
        var diagnostic = Assert.Single(await Analyze(body, returnType: "RailFor<Error>"));

        Assert.Equal("", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["MaySucceed"]);
    }

    [Fact]
    public async Task UntypedRail_UsesTheSameFlowRules()
    {
        var diagnostic = Assert.Single(await Analyze("Rail result = new A(); if (result.TryUnwrap(out var error)) return Rail.Ok(); return error;",
            returnType: "Rail"));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
    }

    [Fact]
    public async Task ExceptionRegions_PreserveKnownReturnsWithAnExplicitApproximation()
    {
        var diagnostic = Assert.Single(await Analyze("try { return new A(); } catch { return new B(); }"));

        Assert.Equal("A, B", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("exception paths", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task RefMutation_PropagatesTheConcreteWrite()
    {
        var diagnostic = Assert.Single(await Analyze("var result = Rail.Fail<int>(new A()); Mutate(ref result); return result;"));

        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Equal("B", diagnostic.Properties["ErrorTypes"]);
    }

    [Fact]
    public async Task Deconstruction_DoesNotKeepThePreviousErrorAsACompleteAnswer()
    {
        var diagnostic = Assert.Single(await Analyze("Error error = new A(); int count; (error, count) = (new B(), 1); return error;"));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("Deconstruction", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task ReferenceAlias_DoesNotLeaveAStaleErrorOnTheOriginalLocal()
    {
        var diagnostic = Assert.Single(await Analyze("var result = Rail.Fail<int>(new A()); ref var alias = ref result; result = new A(); alias = new B(); return result;"));

        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Equal("B", diagnostic.Properties["ErrorTypes"]);
    }

    [Fact]
    public async Task ExcessiveControlFlow_ProducesAnOpenSummaryInsteadOfAPartialCertificate()
    {
        var body = "var result = Rail.Fail<int>(new A());" +
            string.Concat(Enumerable.Repeat("if (flag) result = Rail.Fail<int>(new B());", 2200)) + "return result;";
        var diagnostic = Assert.Single(await Analyze(body));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("budget", diagnostic.Properties["Limitations"]);
    }

    [Theory]
    [InlineData("var result = Rail.Fail<int>(new A()); Action change = () => result = new B(); change(); return result;")]
    [InlineData("var result = Rail.Fail<int>(new A()); Rail.Ok().Tap(() => { result = new B(); }); return result;")]
    [InlineData("var result = Rail.Fail<int>(new A()); Rail.Ok().Tap(() => Mutate(ref result)); return result;")]
    public async Task CapturedMutation_PropagatesSupportedWrites(string body)
    {
        var diagnostic = Assert.Single(await Analyze(body));

        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Equal("B", diagnostic.Properties["ErrorTypes"]);
    }

    [Theory]
    [InlineData("Rail.Ok().Tap(() => { (result, count) = (new B(), 1); });")]
    [InlineData("Rail.Ok().Tap(() => { (result, (count, other)) = (new B(), (1, 2)); });")]
    [InlineData("Action change = () => { (result, count) = (new B(), 1); }; change();")]
    public async Task CapturedDeconstruction_InvalidatesTheCallerState(string mutation)
    {
        var diagnostic = Assert.Single(await Analyze($$"""
            RailFor<int> result = new A();
            int count = 0, other = 0;
            {{mutation}}

            return result;
            """));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("effects", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task CapturedParameterDeconstruction_InvalidatesTheCallerState()
    {
        var diagnostic = Assert.Single(await Analyze("""
            incoming = new A();
            int count = 0;
            Rail.Ok().Tap(() => { (incoming, count) = (new B(), 1); });

            return incoming;
            """, parameters: "RailFor<int> incoming"));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("effects", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task CallbackLocalDeconstruction_DoesNotInvalidateAnUnchangedCallerValue()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> result = new A();
            Rail.Ok().Tap(() =>
            {
                Error localError = new B();
                int count;
                (localError, count) = (new C(), 1);
            });

            return result;
            """));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task ParameterDeconstruction_DoesNotKeepThePreviousErrorAsACompleteAnswer()
    {
        var diagnostic = Assert.Single(await Analyze("""
            incoming = new A();
            int count;
            (incoming, count) = (new B(), 1);

            return incoming;
            """, parameters: "Error incoming"));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("Deconstruction", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task DescriptorCanBeDisabledIndependentlyOfTheFeatureFlag()
    {
        var diagnostics = await Analyze("return new A();", suppress: true);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task CompleteStudyMethod_CompilesAndPreservesTheKnownTypesAlongsideUnknownSources()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Study.cs"));
        var diagnostic = Assert.Single(await AnalyzeSource(source, true, false, "Study"));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Equal("True", diagnostic.Properties["HasUnresolvedConditions"]);
        Assert.Contains("FlowOnRails.Analysis.Sample.AccessDenied", diagnostic.Properties["ErrorTypes"]);
        Assert.Contains("FlowOnRails.Analysis.Sample.UserNotFound", diagnostic.Properties["ErrorTypes"]);
        Assert.Contains("FlowOnRails.Analysis.Sample.PublicFailure", diagnostic.Properties["ErrorTypes"]);
    }

    private static async Task<Diagnostic[]> Analyze(
        string body,
        bool enabled = true,
        string returnType = "RailFor<int>",
        string parameters = "bool flag",
        bool suppress = false,
        string declarations = "",
        bool isAsync = false)
    {
        var source = $$"""
            using System;
            using System.Threading.Tasks;
            using FlowOnRails;
            using FlowOnRails.Extensions.Tasks;
            public record Parent : Error;
            public sealed record A(int Code = 0) : Parent;
            public sealed record B : Error;
            public sealed record C : Error;
            public static class Study
            {
                public static {{(isAsync ? "async " : "")}}{{returnType}} Subject({{parameters}}) { {{body}} }
                private static RailFor<int> Concrete() => new B();
                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
                private static extern RailFor<int> External();
                private static bool Database() => true;
                private static void Mutate(ref RailFor<int> result) => result = new B();
                {{declarations}}
            }
            """;
        return await AnalyzeSource(source, enabled, suppress, "Subject");
    }

    private static async Task<Diagnostic[]> AnalyzeSource(
        string source,
        bool enabled,
        bool suppress,
        string? method,
        ImmutableArray<MetadataReference> additionalReferences = default,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp14), "Study.cs");
        var options = new CSharpCompilationOptions(outputKind,
            nullableContextOptions: NullableContextOptions.Enable,
            specificDiagnosticOptions: suppress ? ImmutableDictionary<string, ReportDiagnostic>.Empty.Add("FORFLOW001", ReportDiagnostic.Suppress) : null);
        var references = additionalReferences.IsDefault ? References : References.AddRange(additionalReferences);
        var compilation = CSharpCompilation.Create("Study", [tree], references, options);
        Assert.DoesNotContain(compilation.GetDiagnostics(), item => item.Severity == DiagnosticSeverity.Error);
        var analyzerOptions = new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty, new OptionsProvider(enabled));
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ErrorFlowAnalyzer()), analyzerOptions)
            .GetAnalyzerDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, item => item.Id == "AD0001");

        if (method is null)
            return diagnostics.Where(item => item.Id is "FORFLOW001" or "FORFLOW002").ToArray();

        return diagnostics.Where(item => item.Id == "FORFLOW001"
                                         && item.Properties["MethodName"] == method).ToArray();
    }

    private sealed class OptionsProvider(bool enabled) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _options = new Options(enabled);
        public override AnalyzerConfigOptions GlobalOptions => _options;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private sealed class Options(bool enabled) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = enabled ? "true" : "false";
            return key == "build_property.FlowOnRailsEnableFlowAnalysis";
        }
    }
}
