using System.Collections.Immutable;

using FlowOnRails.Analyzers;
using FlowOnRails.CodeFixes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;

namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    [Theory]
    [InlineData("Rail.Ok(42)")]
    [InlineData("Rail.Ok()")]
    [InlineData("Rail.Ok(Rail.Fail<int>(new Failure()))")]
    public async Task SuccessFactory_OmitsOrdinaryInfoAndRetainsNestedFailureInfo(string expression)
    {
        var source = $$"""
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Study { public static void Run() { _ = {{expression}}; } }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Properties["MethodName"] == "Ok");
        Assert.Equal(expression.Contains("Fail") ? 1 : 0, diagnostics.Length);
    }

    [Fact]
    public async Task SuccessFactory_ErrorPayloadHasAnExplicitSuccessMessage()
    {
        const string source = """
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Study { public static void Run() { _ = Rail.Ok<Error>(new Failure()); } }
            """;
        var diagnostic = Assert.Single(await AnalyzeSource(source, true, false, null));

        Assert.Equal("FORFLOW002", diagnostic.Id);
        Assert.Equal("True", diagnostic.Properties["SuccessValueIsError"]);
        Assert.Contains("successful rail with an Error as its value", diagnostic.GetMessage());
        Assert.Equal("", diagnostic.Properties["ErrorTypes"]);
    }

    [Fact]
    public async Task ResultsWithFix_EditsAnotherProjectAndKeepsImplementationErrorsOutOfTheContract()
    {
        using var workspace = new AdhocWorkspace();
        var contracts = AddEditorProject(workspace.CurrentSolution, "Contracts");
        var contractDocument = contracts.AddDocument("Contracts.cs", SourceText.From("""
            using FlowOnRails;
            public sealed record KnownFailure : Error;
            public interface IReader { RailFor<int> Read(bool flag); }
            """));
        var services = AddEditorProject(contractDocument.Project.Solution, "Services")
            .AddProjectReference(new ProjectReference(contracts.Id));
        var implementation = services.AddDocument("Reader.cs", SourceText.From("""
            using FlowOnRails;
            public sealed record ImplementationFailure : Error;
            public sealed class Reader : IReader
            {
                public RailFor<int> Read(bool flag) => flag ? new KnownFailure() : new ImplementationFailure();
            }
            """));
        var actions = await ContractActions(implementation, "Read");
        var changed = await ApplyEditorAction(Assert.Single(actions));
        var contractText = (await changed.GetDocument(contractDocument.Id)!.GetTextAsync()).ToString();

        Assert.Contains("ResultsWith<KnownFailure>", contractText);
        Assert.DoesNotContain("ImplementationFailure", contractText);
        Assert.Empty(changed.GetProject(contracts.Id)!.ProjectReferences);
        await AssertEditorSolutionCompiles(changed);
        Assert.Empty(await ContractActions(changed.GetDocument(implementation.Id)!, "Read"));

        var contractCompilation = (await changed.GetProject(contracts.Id)!.GetCompilationAsync())!;
        var diagnostics = await contractCompilation.WithAnalyzers(
            [new ErrorFlowAnalyzer()], new AnalyzerOptions([], new OptionsProvider(true))).GetAnalyzerDiagnosticsAsync();
        Assert.Equal("True", Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW001")
            .Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task EditorActions_WorkAcrossContractImplementationAndConsumerProjects()
    {
        using var workspace = new AdhocWorkspace();
        var contracts = AddEditorProject(workspace.CurrentSolution, "Contracts");
        var contractDocument = contracts.AddDocument("Contracts.cs", SourceText.From("""
            using FlowOnRails;
            namespace IdeLab.Contracts;
            public record KnowError : Error;
            public sealed record UserNotFound : Error;
            public sealed record AccessDenied : Error;
            public interface IReader
            {
                [ResultsWith<UserNotFound>]
                RailFor<int> Read(int userId);
            }
            """));
        var services = AddEditorProject(contractDocument.Project.Solution, "Services")
            .AddProjectReference(new ProjectReference(contracts.Id));
        var implementation = services.AddDocument("Reader.cs", SourceText.From("""
            using FlowOnRails;
            using IdeLab.Contracts;
            public sealed record DatabaseUnavailable : Error;
            public sealed class Reader : IReader
            {
                public RailFor<int> Read(int userId)
                {
                    if (userId == 0) return new UserNotFound();
                    if (userId < 0) return new AccessDenied();
                    if (userId < 13) return new KnowError();
                    return new DatabaseUnavailable();
                }
            }
            """));
        var consumer = AddEditorProject(implementation.Project.Solution, "Consumer")
            .AddProjectReference(new ProjectReference(contracts.Id));
        var consumerDocument = consumer.AddDocument("EditorActions.cs", SourceText.From("""
            using FlowOnRails;
            using IdeLab.Contracts;
            public static class EditorActions
            {
                public static int Handle(IReader reader, int userId)
                {
                    if (reader.Read(userId).TryUnwrap(out var value, out var error)) return value;
                    return error switch { _ => -1 };
                }
            }
            """));
        var solution = consumerDocument.Project.Solution;
        var contractAction = Assert.Single(await ContractActions(solution.GetDocument(implementation.Id)!, "Read"));
        var changed = await ApplyEditorAction(contractAction);
        var contractText = (await changed.GetDocument(contractDocument.Id)!.GetTextAsync()).ToString();

        Assert.Contains("ResultsWith<KnowError>", contractText);
        Assert.Contains("ResultsWith<AccessDenied>", contractText);
        Assert.DoesNotContain("DatabaseUnavailable", contractText);

        var changedConsumer = changed.GetDocument(consumerDocument.Id)!;
        var consumerText = (await changedConsumer.GetTextAsync()).ToString();
        var switchAction = Assert.Single(await SwitchActions(changedConsumer, consumerText.IndexOf("switch", StringComparison.Ordinal)));
        var completed = await ApplyEditorAction(switchAction);
        var completedText = (await completed.GetDocument(consumerDocument.Id)!.GetTextAsync()).ToString();

        Assert.Contains("KnowError => throw", completedText);
        Assert.Contains("AccessDenied => throw", completedText);
        Assert.Contains("UserNotFound => throw", completedText);
        Assert.DoesNotContain("DatabaseUnavailable", completedText);
        Assert.Contains("_ => -1", completedText);
        await AssertEditorSolutionCompiles(completed);
    }

    [Fact]
    public async Task ResultsWithFix_AddsMissingTypesToEveryImplementedContractWithoutDuplicates()
    {
        using var workspace = new AdhocWorkspace();
        var document = AddEditorProject(workspace.CurrentSolution, "Study").AddDocument("Study.cs", SourceText.From("""
            using FlowOnRails;
            public sealed record First : Error;
            public sealed record Second : Error;
            public interface IFirst { [ResultsWith<First>] RailFor<int> Read(bool flag); }
            public interface ISecond { RailFor<int> Read(bool flag); }
            public sealed class Reader : IFirst, ISecond
            {
                public RailFor<int> Read(bool flag) => flag ? new First() : new Second();
            }
            """));
        var changed = await ApplyEditorAction(Assert.Single(await ContractActions(document, "Read")));
        var text = (await changed.GetDocument(document.Id)!.GetTextAsync()).ToString();

        Assert.Equal(2, text.Split("ResultsWith<First>").Length - 1);
        Assert.Equal(2, text.Split("ResultsWith<Second>").Length - 1);
        await AssertEditorSolutionCompiles(changed);
    }

    [Theory]
    [InlineData("public async System.Threading.Tasks.Task<RailFor<int>> Read() { await System.Threading.Tasks.Task.Yield(); return new GenericFailure<int>(); }", "System.Threading.Tasks.Task<RailFor<int>> Read();", "Read")]
    [InlineData("RailFor<int> IReader.Read() => new GenericFailure<int>();", "RailFor<int> Read();", "IReader.Read")]
    public async Task ResultsWithFix_SupportsAsyncAndExplicitImplementations(
        string implementation,
        string contract,
        string methodName)
    {
        using var workspace = new AdhocWorkspace();
        var document = AddEditorProject(workspace.CurrentSolution, "Study").AddDocument("Study.cs", SourceText.From($$"""
            using FlowOnRails;
            public sealed record GenericFailure<TValue> : Error;
            public interface IReader { {{contract}} }
            public sealed class Reader : IReader { {{implementation}} }
            """));
        var changed = await ApplyEditorAction(Assert.Single(await ContractActions(document, methodName)));

        Assert.Contains("ResultsWith<GenericFailure<int>>", (await changed.GetDocument(document.Id)!.GetTextAsync()).ToString());
        await AssertEditorSolutionCompiles(changed);
    }

    [Theory]
    [InlineData("private sealed record Hidden : Error; public RailFor<int> Read() => new Hidden();")]
    [InlineData("public RailFor<int> Read() => Rail.Ok(42);")]
    public async Task ResultsWithFix_DoesNotOfferUnavailableOrAbsentErrors(string members)
    {
        using var workspace = new AdhocWorkspace();
        var document = AddEditorProject(workspace.CurrentSolution, "Study").AddDocument("Study.cs", SourceText.From($$"""
            using FlowOnRails;
            public interface IReader { RailFor<int> Read(); }
            public sealed class Reader : IReader { {{members}} }
            """));

        Assert.Empty(await ContractActions(document, "Read"));
    }

    [Theory]
    [InlineData("return error $$switch { First => 1, _ => 0 };", "Second => throw new NotImplementedException()")]
    [InlineData("$$switch (error) { case First: return 1; default: return 0; }", "case Second:")]
    [InlineData("return error $$switch { First when flag => 1, _ => 0 };", "First => throw new NotImplementedException()")]
    [InlineData("return error $$switch { Error remaining => 0 };", "Second => throw new NotImplementedException()")]
    [InlineData("return error $$switch { var remaining when true => 0 };", "Second => throw new NotImplementedException()")]
    [InlineData("$$switch (error) { default: return 0; case First: return 1; }", "case Second:")]
    public async Task SwitchAction_AddsMissingKnownCasesAndPreservesFallback(string switchCode, string expectedCase)
    {
        using var workspace = new AdhocWorkspace();
        var document = SwitchDocument(workspace, switchCode, out var position);
        var actions = await SwitchActions(document, position);
        var changed = await ApplyEditorAction(Assert.Single(actions));
        var text = (await changed.GetDocument(document.Id)!.GetTextAsync()).ToString();

        Assert.True(text.Contains(expectedCase), text);
        var expectedFallback = "_ => 0";

        if (switchCode.Contains("default"))
            expectedFallback = "default:";
        else if (switchCode.Contains("Error remaining"))
            expectedFallback = "Error remaining => 0";
        else if (switchCode.Contains("when true"))
            expectedFallback = "var remaining when true => 0";

        Assert.Contains(expectedFallback, text);

        if (switchCode.Contains("when flag"))
            Assert.Contains("when flag", text);

        await AssertEditorSolutionCompiles(changed);
        var changedDocument = changed.GetDocument(document.Id)!;
        Assert.Empty(await SwitchActions(changedDocument, text.IndexOf("switch", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task SwitchAction_UsesFlowAtTheSwitchInsteadOfAllMethodErrors()
    {
        using var workspace = new AdhocWorkspace();
        var document = SwitchDocument(workspace, """
            if (error is First) return 1;
            return error $$switch { _ => 0 };
            """, out var position);
        var changed = await ApplyEditorAction(Assert.Single(await SwitchActions(document, position)));
        var text = (await changed.GetDocument(document.Id)!.GetTextAsync()).ToString();

        Assert.Contains("Second => throw", text);
        Assert.DoesNotContain("First =>", text);
        await AssertEditorSolutionCompiles(changed);
    }

    [Fact]
    public async Task SwitchAction_KeepsOpenInterfaceRemainder()
    {
        using var workspace = new AdhocWorkspace();
        var source = """
            using FlowOnRails;
            public sealed record First : Error;
            public interface IReader { [ResultsWith<First>] RailFor<int> Read(); }
            public static class Study
            {
                public static int Run(IReader reader)
                {
                    if (reader.Read().TryUnwrap(out _, out var error)) return 42;
                    return error $$switch { _ => -1 };
                }
            }
            """;
        var position = source.IndexOf("$$", StringComparison.Ordinal);
        var document = AddEditorProject(workspace.CurrentSolution, "Study").AddDocument("Study.cs", SourceText.From(source.Replace("$$", "")));
        var changed = await ApplyEditorAction(Assert.Single(await SwitchActions(document, position)));
        var text = (await changed.GetDocument(document.Id)!.GetTextAsync()).ToString();

        Assert.Contains("First => throw", text);
        Assert.Contains("_ => -1", text);
        Assert.DoesNotContain("Error =>", text);
        await AssertEditorSolutionCompiles(changed);
    }

    [Theory]
    [InlineData("return error $$switch { First => 1, Second => 2, _ => 0 };")]
    [InlineData("return error $$switch { First when true => 1, Second => 2, _ => 0 };")]
    [InlineData("return error $$switch { First { } => 1, Second => 2, _ => 0 };")]
    [InlineData("return error $$switch { not First => 1, _ => 0 };")]
    public async Task SwitchAction_IsAbsentWhenCompleteOrUnsupported(string switchCode)
    {
        using var workspace = new AdhocWorkspace();
        var document = SwitchDocument(workspace, switchCode, out var position);

        Assert.Empty(await SwitchActions(document, position));
    }

    [Fact]
    public async Task SwitchAction_ReadsKnownErrorsFromAnotherAssemblySummary()
    {
        using var workspace = new AdhocWorkspace();
        var reference = EmitProducer(ProducerSource, true, out _);
        const string source = """
            using FlowOnRails;
            using Producer;
            public static class Study
            {
                public static int Run()
                {
                    if (Api.Direct().TryUnwrap(out var value, out var error)) return value;
                    return error $$switch { _ => -1 };
                }
            }
            """;
        var position = source.IndexOf("$$", StringComparison.Ordinal);
        var document = AddEditorProject(workspace.CurrentSolution, "Consumer").AddMetadataReference(reference)
            .AddDocument("Consumer.cs", SourceText.From(source.Replace("$$", "")));
        var changed = await ApplyEditorAction(Assert.Single(await SwitchActions(document, position)));
        var text = (await changed.GetDocument(document.Id)!.GetTextAsync()).ToString();

        Assert.Contains("FirstFailure => throw", text);
        Assert.Contains("_ => -1", text);
        await AssertEditorSolutionCompiles(changed);
    }

    [Fact]
    public async Task SwitchAction_SupportsTopLevelStatements()
    {
        using var workspace = new AdhocWorkspace();
        const string source = """
            using FlowOnRails;
            RailFor<int> result = new Failure();
            if (!result.TryUnwrap(out _, out var error))
                _ = error $$switch { _ => 0 };
            public sealed record Failure : Error;
            """;
        var position = source.IndexOf("$$", StringComparison.Ordinal);
        var project = AddEditorProject(workspace.CurrentSolution, "Study")
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        var document = project.AddDocument("Program.cs", SourceText.From(source.Replace("$$", "")));
        var changed = await ApplyEditorAction(Assert.Single(await SwitchActions(document, position)));

        Assert.Contains("Failure => throw", (await changed.GetDocument(document.Id)!.GetTextAsync()).ToString());
        await AssertEditorSolutionCompiles(changed);
    }

    [Fact]
    public async Task SwitchAction_PlacesDerivedTypesBeforeBaseTypesAndKeepsGuardedFallbacks()
    {
        using var workspace = new AdhocWorkspace();
        const string source = """
            using FlowOnRails;
            public record Parent : Error;
            public sealed record Child : Parent;
            public static class Study
            {
                public static int Run(bool flag)
                {
                    Error error = flag ? new Parent() : new Child();
                    return error $$switch { var remaining when flag => 1, _ => 0 };
                }
            }
            """;
        var position = source.IndexOf("$$", StringComparison.Ordinal);
        var document = AddEditorProject(workspace.CurrentSolution, "Study").AddDocument("Study.cs", SourceText.From(source.Replace("$$", "")));
        var changed = await ApplyEditorAction(Assert.Single(await SwitchActions(document, position)));
        var text = (await changed.GetDocument(document.Id)!.GetTextAsync()).ToString();

        Assert.True(text.IndexOf("Child =>", StringComparison.Ordinal) < text.IndexOf("Parent =>", StringComparison.Ordinal));
        Assert.True(text.IndexOf("when flag", StringComparison.Ordinal) < text.IndexOf("Child =>", StringComparison.Ordinal));
        await AssertEditorSolutionCompiles(changed);
    }

    private static Project AddEditorProject(Solution solution, string name)
    {
        var project = solution.AddProject(name, name, LanguageNames.CSharp)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.CSharp14))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(References);

        return project.AddAnalyzerConfigDocument("Flow.globalconfig", SourceText.From("""
            is_global = true
            build_property.FlowOnRailsEnableFlowAnalysis = true
            """), filePath: "/" + name + "/Flow.globalconfig").Project;
    }

    private static Document SwitchDocument(AdhocWorkspace workspace, string switchCode, out int position)
    {
        var source = $$"""
            using System;
            using FlowOnRails;
            public sealed record First : Error;
            public sealed record Second : Error;
            public static class Study
            {
                public static int Run(bool flag)
                {
                    RailFor<int> result = flag ? new First() : new Second();
                    if (result.TryUnwrap(out _, out var error)) return 42;
                    {{switchCode}}
                }
            }
            """;
        position = source.IndexOf("$$", StringComparison.Ordinal);

        return AddEditorProject(workspace.CurrentSolution, "Study").AddDocument("Study.cs", SourceText.From(source.Replace("$$", "")));
    }

    private static async Task<List<CodeAction>> ContractActions(Document document, string methodName)
    {
        var compilation = (await document.Project.GetCompilationAsync())!;
        var diagnostics = await compilation.WithAnalyzers(
            [new ErrorFlowAnalyzer()], new AnalyzerOptions([], new OptionsProvider(true))).GetAnalyzerDiagnosticsAsync();
        var tree = await document.GetSyntaxTreeAsync();
        var root = (await document.GetSyntaxRootAsync())!;
        var diagnostic = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "FORFLOW001"
            && diagnostic.Properties["MethodName"] == methodName
            && diagnostic.Location.SourceTree == tree
            && root.FindNode(diagnostic.Location.SourceSpan).AncestorsAndSelf().OfType<ClassDeclarationSyntax>().Any());
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await new ResultsWithCodeFixProvider().RegisterCodeFixesAsync(context);

        return actions;
    }

    private static async Task<List<CodeAction>> SwitchActions(Document document, int position)
    {
        var actions = new List<CodeAction>();
        var compilation = (await document.Project.GetCompilationAsync())!;
        var diagnostics = await compilation.WithAnalyzers(
            [new ErrorFlowAnalyzer()], new AnalyzerOptions([], new OptionsProvider(true))).GetAnalyzerDiagnosticsAsync();
        var diagnostic = diagnostics.SingleOrDefault(diagnostic => diagnostic.Id == "FORFLOW003"
            && diagnostic.Location.SourceSpan.Contains(position));

        if (diagnostic is not null)
        {
            Assert.Equal(DiagnosticSeverity.Hidden, diagnostic.Severity);
            await new CompleteErrorSwitchCodeFixProvider().RegisterCodeFixesAsync(
                new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        }

        return actions;
    }

    private static async Task<Solution> ApplyEditorAction(CodeAction action)
    {
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var solution = Assert.Single(operations.OfType<ApplyChangesOperation>()).ChangedSolution;

        foreach (var projectId in solution.ProjectIds)
        foreach (var documentId in solution.GetProject(projectId)!.DocumentIds)
        {
            var document = solution.GetDocument(documentId)!;
            document = await Simplifier.ReduceAsync(document);
            document = await Formatter.FormatAsync(document);
            solution = document.Project.Solution;
        }

        return solution;
    }

    private static async Task AssertEditorSolutionCompiles(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = (await project.GetCompilationAsync())!;
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
    }
}
