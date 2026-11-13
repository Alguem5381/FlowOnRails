using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    [Fact]
    public async Task InvocationInfo_ReportsCallsInTopLevelStatements()
    {
        const string source = """
            using FlowOnRails;
            _ = Rail.Fail<int>(new Failure());
            _ = Rail.Ok(new Failure());
            public sealed record Failure : Error;
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null, outputKind: OutputKind.ConsoleApplication);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("FORFLOW002", diagnostic.Id));
        Assert.Equal("Failure", Assert.Single(diagnostics,
            diagnostic => diagnostic.Properties["MethodName"] == "Fail").Properties["ErrorTypes"]);
        Assert.Equal("", Assert.Single(diagnostics,
            diagnostic => diagnostic.Properties["MethodName"] == "Ok").Properties["ErrorTypes"]);
    }

    [Theory]
    [InlineData("Rail.Fail(new Failure())", "Fail", "Failure")]
    [InlineData("Rail.Fail<int>(new Failure())", "Fail", "Failure")]
    [InlineData("Rail.Ok(new Failure())", "Ok", "")]
    public async Task InvocationInfo_IncludesFailAndOkWithoutTreatingSuccessPayloadsAsErrors(
        string expression,
        string methodName,
        string expectedErrors)
    {
        var source = $$"""
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Study
            {
                public static void Subject() { _ = {{expression}}; }
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var invocation = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW002"
                                                                 && diagnostic.Properties["MethodName"] == methodName);

        Assert.Equal(expectedErrors, invocation.Properties["ErrorTypes"]);
        Assert.Equal("False", invocation.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task Message_GroupsNamespacesAndCompactsUnbranchedPrefixes()
    {
        const string source = """
            using FlowOnRails;
            namespace Company.Product.IdeLab.Contracts
            {
                public sealed record AccessDenied : Error;
                public sealed record UserNotFound : Error;
            }
            namespace Company.Product.IdeLab.Services
            {
                public sealed record DatabaseUnavailable : Error;
            }
            public static class Study
            {
                public static RailFor<int> Subject(int choice) => choice switch
                {
                    0 => new Company.Product.IdeLab.Contracts.AccessDenied(),
                    1 => new Company.Product.IdeLab.Contracts.UserNotFound(),
                    _ => new Company.Product.IdeLab.Services.DatabaseUnavailable()
                };
            }
            """;
        var diagnostic = Assert.Single(await AnalyzeSource(source, true, false, "Subject"));

        Assert.Equal("""
            This method may return errors:

            Company.Product.IdeLab:
                Contracts:
                    AccessDenied
                    UserNotFound
                Services:
                    DatabaseUnavailable

            No additional error types were found in the supported flow.

            Some conditions could not be determined, so the list of possible errors may be broader than necessary.
            """, diagnostic.GetMessage().ReplaceLineEndings("\n"));
        Assert.DoesNotContain("Subject", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Message_PreservesGenericArgumentsAndNestedTypes()
    {
        const string source = """
            using FlowOnRails;
            namespace Models { public sealed class Token; }
            namespace Acme.Errors
            {
                public sealed record Failure<TValue> : Error;
                public static class Holder { public sealed record Nested : Error; }
            }
            public static class Study
            {
                public static RailFor<int> Subject(bool flag) => flag
                    ? new Acme.Errors.Failure<Models.Token>()
                    : new Acme.Errors.Holder.Nested();
            }
            """;
        var diagnostic = Assert.Single(await AnalyzeSource(source, true, false, "Subject"));

        Assert.Contains("Acme.Errors:\n    Failure<Models.Token>\n    Holder.Nested\n", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Message_DistinguishesUnknownErrorsFromNoReturnedErrors()
    {
        var unknown = Assert.Single(await Analyze("return External();"));
        var success = Assert.Single(await Analyze("return Rail.Ok(1);"));

        Assert.StartsWith("No known error types were identified for this method.", unknown.GetMessage());
        Assert.Contains("Other error types may be returned.", unknown.GetMessage());
        Assert.DoesNotContain("FlowOnRails.Error", unknown.GetMessage());
        Assert.Equal("FlowOnRails.Error (+ subtypes)", unknown.Properties["ErrorTypes"]);
        Assert.StartsWith("No returned errors were identified for this method.", success.GetMessage());
        Assert.Equal("No returned errors were identified for this method.", success.GetMessage());
    }

    [Fact]
    public async Task Message_KeepsExclusionsFromTheHiddenUnknownFamily()
    {
        var diagnostic = Assert.Single(await Analyze("""
            var result = External();
            if (!result.TryUnwrap(out _, out var error) && error is A)
                return Rail.Ok(1);

            return result;
            """));

        Assert.DoesNotContain("FlowOnRails.Error", diagnostic.GetMessage());
        Assert.Contains("Unknown error alternatives:\n    Other errors except A", diagnostic.GetMessage());
    }

    [Fact]
    public async Task InvocationInfo_RemainsAtTheCallAfterTheCallerRecoversItsError()
    {
        const string source = """
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Study
            {
                public static RailFor<int> Subject() => Read().Recover(error => 42);
                private static RailFor<int> Read() => new Failure();
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var invocation = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW002"
                                                                 && diagnostic.Properties["MethodName"] == "Read");
        var declaration = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW001"
                                                                  && diagnostic.Properties["MethodName"] == "Subject");

        Assert.Equal("Failure", invocation.Properties["ErrorTypes"]);
        Assert.Equal("Read", source.Substring(invocation.Location.SourceSpan.Start, invocation.Location.SourceSpan.Length));
        Assert.Equal("Invocation", invocation.Properties["TargetKind"]);
        Assert.Equal("", declaration.Properties["ErrorTypes"]);
        Assert.Equal("Declaration", declaration.Properties["TargetKind"]);
    }

    [Theory]
    [InlineData("public static void Subject() { var result = Api.Read(); }")]
    [InlineData("public static int Subject() { var result = Api.Read(); return 42; }")]
    [InlineData("public Study() { var result = Api.Read(); }")]
    [InlineData("public int Subject { get { var result = Api.Read(); return 42; } }")]
    [InlineData("public static async System.Threading.Tasks.Task Subject() { var result = Api.Read(); await System.Threading.Tasks.Task.Yield(); }")]
    public async Task InvocationInfo_IsAvailableOutsideRailReturningMethods(string member)
    {
        var source = $$"""
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Api { public static RailFor<int> Read() => new Failure(); }
            public sealed class Study { {{member}} }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var invocation = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW002");

        Assert.Equal("Failure", invocation.Properties["ErrorTypes"]);
        Assert.Equal("Read", source.Substring(invocation.Location.SourceSpan.Start, invocation.Location.SourceSpan.Length));
    }

    [Fact]
    public async Task InvocationInfo_UsesEachCallsArgumentsWithoutDuplicatingCalleeDeclarations()
    {
        const string source = """
            using FlowOnRails;
            public sealed record First : Error;
            public sealed record Second : Error;
            public static class Study
            {
                public static void Subject()
                {
                    _ = Pick(true);
                    _ = Pick(false);
                }
                private static RailFor<int> Pick(bool first) => first ? new First() : new Second();
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var calls = diagnostics.Where(diagnostic => diagnostic.Id == "FORFLOW002")
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToArray();

        Assert.Equal(2, calls.Length);
        Assert.Equal("First", calls[0].Properties["ErrorTypes"]);
        Assert.Equal("Second", calls[1].Properties["ErrorTypes"]);
        Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW001");
    }

    [Fact]
    public async Task InvocationInfo_SeparatesOpenInterfaceContractsFromConcreteBodies()
    {
        const string source = """
            using FlowOnRails;
            public sealed record KnownFailure : Error;
            public sealed record ImplementationFailure : Error;
            public interface IReader
            {
                [ResultsWith<KnownFailure>] RailFor<int> Read();
            }
            public sealed class Reader : IReader
            {
                public RailFor<int> Read() => new ImplementationFailure();
            }
            public static class Study
            {
                public static void Subject(IReader reader)
                {
                    _ = reader.Read();
                    _ = new Reader().Read();
                }
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var calls = diagnostics.Where(diagnostic => diagnostic.Id == "FORFLOW002")
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToArray();

        Assert.Equal(2, calls.Length);
        Assert.Equal("True", calls[0].Properties["HasUnknownErrors"]);
        Assert.Contains("KnownFailure", calls[0].GetMessage());
        Assert.DoesNotContain("ImplementationFailure", calls[0].GetMessage());
        Assert.DoesNotContain("FlowOnRails.Error", calls[0].GetMessage());
        Assert.Equal("False", calls[1].Properties["HasUnknownErrors"]);
        Assert.Equal("ImplementationFailure", calls[1].Properties["ErrorTypes"]);
        Assert.Equal("", calls[1].Properties["Limitations"]);
    }

    [Fact]
    public async Task InvocationInfo_JoinsLoopVisitsIntoOneDiagnosticPerCall()
    {
        const string source = """
            using FlowOnRails;
            public sealed record First : Error;
            public sealed record Second : Error;
            public static class Study
            {
                public static void Subject(bool repeat)
                {
                    RailFor<int> result = new First();
                    while (repeat)
                    {
                        _ = Forward(result);
                        result = new Second();
                    }
                }
                private static RailFor<int> Forward(RailFor<int> result) => result;
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var invocation = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW002");

        Assert.Equal("First, Second", invocation.Properties["ErrorTypes"]);
        Assert.Equal("False", invocation.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task InvocationInfo_UsesGenericAndAsyncSummariesFromAnotherAssembly()
    {
        var reference = EmitProducer(ProducerSource, true, out _);
        const string source = """
            using FlowOnRails;
            using Producer;
            public static class Study
            {
                public static async System.Threading.Tasks.Task Subject()
                {
                    _ = Api.Forward<int>(Rail.Fail<int>(new FirstFailure()));
                    _ = await Api.AsyncFactory();
                }
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null, [reference]);
        var forwarding = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW002"
                                                                 && diagnostic.Properties["MethodName"] == "Forward");
        var asynchronous = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW002"
                                                                   && diagnostic.Properties["MethodName"] == "AsyncFactory");

        Assert.Equal("Producer.FirstFailure", forwarding.Properties["ErrorTypes"]);
        Assert.Equal("Forward", source.Substring(forwarding.Location.SourceSpan.Start, forwarding.Location.SourceSpan.Length));
        Assert.Equal("Producer.SecondFailure", asynchronous.Properties["ErrorTypes"]);
    }

    [Fact]
    public async Task InvocationInfo_SurvivesSuppressionOfDeclarationInfo()
    {
        const string source = """
            using FlowOnRails;
            public static class Study
            {
                public static RailFor<int> Subject() => Read();
                private static RailFor<int> Read() => Rail.Ok(42);
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, true, null);

        Assert.Equal("FORFLOW002", Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task DisabledAnalysis_ProducesNeitherDeclarationNorInvocationInfo()
    {
        const string source = """
            using FlowOnRails;
            public static class Study
            {
                public static RailFor<int> Subject() => Rail.Ok(42);
            }
            """;

        Assert.Empty(await AnalyzeSource(source, false, false, null));
    }

    [Fact]
    public async Task InvocationInfo_TracksCallsInsideExceptionRegions()
    {
        const string source = """
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Study
            {
                public static void Subject()
                {
                    try { _ = Rail.Fail<int>(new Failure()); }
                    catch (System.Exception) { }
                }
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var invocation = Assert.Single(diagnostics);

        Assert.Equal("FORFLOW002", invocation.Id);
        Assert.Equal("False", invocation.Properties["HasUnknownErrors"]);
        Assert.Equal("Failure", invocation.Properties["ErrorTypes"]);
        Assert.DoesNotContain("Call-site flow is unavailable", invocation.Properties["Limitations"]);
    }

    [Fact]
    public async Task InvocationInfo_ObservesCallsInsideInterpretedInlineCallbacks()
    {
        const string source = """
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Study
            {
                public static RailFor<int> Subject() => Rail.Ok(1).Bind(value => Read());
                private static RailFor<int> Read() => new Failure();
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var invocation = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW002"
                                                                 && diagnostic.Properties["MethodName"] == "Read");

        Assert.Equal("Failure", invocation.Properties["ErrorTypes"]);
        Assert.Equal("", invocation.Properties["Limitations"]);
    }

    [Fact]
    public async Task InvocationInfo_JoinsRepeatedInlineCallbackObservations()
    {
        const string source = """
            using FlowOnRails;
            public sealed record First : Error;
            public sealed record Second : Error;
            public static class Study
            {
                public static void Subject(bool repeat)
                {
                    RailFor<int> result = new First();
                    while (repeat)
                    {
                        _ = Rail.Ok(1).Bind(value => Forward(result));
                        result = new Second();
                    }
                }
                private static RailFor<int> Forward(RailFor<int> result) => result;
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var invocation = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW002"
                                                                 && diagnostic.Properties["MethodName"] == "Forward");

        Assert.Equal("First, Second", invocation.Properties["ErrorTypes"]);
        Assert.Equal("False", invocation.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task InvocationInfo_LocatesConditionalMemberAccessOnItsMethodName()
    {
        const string source = """
            using FlowOnRails;
            public sealed record Failure : Error;
            public interface IReader { [ResultsWith<Failure>] RailFor<int> Read(); }
            public static class Study
            {
                public static void Subject(IReader? reader) { _ = reader?.Read(); }
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var invocation = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW002");

        Assert.Equal("Read", source.Substring(invocation.Location.SourceSpan.Start, invocation.Location.SourceSpan.Length));
        Assert.Contains("Failure", invocation.GetMessage());
        Assert.Equal("True", invocation.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task InvocationInfo_DoesNotCopyAnEarlierCallsLimitationsOrConditions()
    {
        const string source = """
            using FlowOnRails;
            public interface IReader { RailFor<int> Read(); }
            public sealed record Failure : Error;
            public static class Study
            {
                public static void Subject(IReader reader, bool flag)
                {
                    _ = reader.Read();
                    if (flag) { _ = Rail.Fail<int>(new Failure()); }
                }
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var invocation = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW002"
                                                                 && diagnostic.Properties["MethodName"] == "Fail");

        Assert.Equal("False", invocation.Properties["HasUnknownErrors"]);
        Assert.Equal("False", invocation.Properties["HasUnresolvedConditions"]);
        Assert.Equal("", invocation.Properties["Limitations"]);
    }

    [Theory]
    [InlineData("private static readonly RailFor<int> Result = Rail.Fail<int>(new Failure());")]
    [InlineData("public static RailFor<int> Result { get; } = Rail.Fail<int>(new Failure());")]
    public async Task InvocationInfo_ReportsAnExplicitLimitForInitializerContexts(string member)
    {
        var source = $$"""
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Study { {{member}} }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var invocation = Assert.Single(diagnostics);

        Assert.Equal("FORFLOW002", invocation.Id);
        Assert.Equal("Fail", source.Substring(invocation.Location.SourceSpan.Start, invocation.Location.SourceSpan.Length));
        Assert.Equal("True", invocation.Properties["HasUnknownErrors"]);
        Assert.Contains("Call-site flow is unavailable", invocation.Properties["Limitations"]);
    }
}
