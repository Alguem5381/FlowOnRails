using FlowOnRails.AspNetCore.Generators;
using FlowOnRails.AspNetCore.Translation;
using FlowOnRails.TestDomain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FlowOnRails.AspNetCore.Generators.Tests;

public class GeneratorTests
{
    private const string Imports = """
        using FlowOnRails;
        using FlowOnRails.AspNetCore.Translation;
        using FlowOnRails.TestDomain;
        namespace Consumer;
        """;

    [Fact]
    public void ReferencedDomainAndClosedGenerics_ProduceCompilableExactProjectors()
    {
        var source = Imports + """
            [ErrorAssembly(typeof(DomainError))]
            [ErrorAssembly(typeof(UserNotFound))]
            public static partial class Problems
            {
                [HttpProblem(404), AutomaticExtensions, UseDataField]
                private static void ArbitraryName(ProblemBuilder<UserNotFound> p) => p.Title("Missing");
                [HttpProblem(422), AutomaticExtensions]
                private static void Generic(ProblemBuilder<ValidationError<int>> p) { }
                [GenericProblem]
                private static void Fallback(GenericProblemBuilder p) { }
            }
            """;
        var result = Generate(source);

        AssertNoErrors(result);
        Assert.DoesNotContain("global::FlowOnRails.TestDomain.DomainNotFound", result.Code);
        Assert.Contains("catalog.Add<global::FlowOnRails.TestDomain.UserNotFound>", result.Code);
        Assert.Contains("global::FlowOnRails.TestDomain.ValidationError<int>", result.Code);
        Assert.Contains("@ArbitraryName", result.Code);
        Assert.Contains("__FlowOnRailsRegister", result.Code);
    }

    [Theory]
    [InlineData("file record Unmapped : Error;")]
    [InlineData("file class Hidden { public record Unmapped : Error; }")]
    [InlineData("[System.Obsolete(\"Retired\", true)] public record Unmapped : Error;")]
    [InlineData("[System.Obsolete(\"Retired\")] public record Unmapped : Error;")]
    public void UnmappedContracts_DoNotIntroduceReferencesOrCompilerDiagnostics(string declaration)
    {
        var result = Generate(Imports + declaration + """
            public record E : Error;
            public static partial class Problems {
                [HttpProblem(400)] private static void Configure(ProblemBuilder<E> p) { }
            }
            """);

        AssertNoErrors(result);
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(), item => item.Id is "CS0618" or "CS0619");
        Assert.DoesNotContain("Unmapped", result.Code);
        Assert.Contains("catalog.Add<global::Consumer.E>", result.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitlyMappedObsoleteContracts_AreGeneratedWithNativeCompilerDiagnostics(bool error)
    {
        var result = Generate(Imports + $$"""
            [System.Obsolete("Use ReplacementError instead.", error: {{(error ? "true" : "false")}})]
            public record RetiredError(int Code) : Error;
            public static partial class Problems {
                [HttpProblem(410), AutomaticExtensions]
                private static void Configure(ProblemBuilder<RetiredError> p) { }
            }
            """);

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Contains("catalog.Add<global::Consumer.RetiredError>", result.Code);
        Assert.Contains("error.@Code;", result.Code);
        Assert.Contains(result.Compilation.GetDiagnostics(), item =>
            item.Location.SourceTree?.FilePath == "Source0.cs" &&
            item.Id == (error ? "CS0619" : "CS0618") &&
            item.Severity == (error ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning));

        if (!error)
            AssertNoErrors(result);
    }

    [Theory]
    [InlineData("private static int Wrong(ProblemBuilder<E> p) => 1;", "001")]
    [InlineData("private static async void Wrong(ProblemBuilder<E> p) { await System.Threading.Tasks.Task.Yield(); }", "001")]
    [InlineData("private static void Wrong<T>(ProblemBuilder<E> p) { }", "001")]
    [InlineData("private static void Wrong(ref ProblemBuilder<E> p) { }", "001")]
    [InlineData("private static void Wrong(ProblemBuilder<E> p, int other) { }", "001")]
    [InlineData("private static void Wrong(string p) { }", "001")]
    public void InvalidSignatures_AreReportedAtUserDeclarations(string method, string id)
    {
        var result = Generate(Imports + "public record E : Error; public static partial class Problems { [HttpProblem(400)] " + method + " }");

        var diagnostic = Assert.Single(result.GeneratorDiagnostics, item => item.Id == "FORHTTP" + id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource);
    }

    [Fact]
    public void NonPartialContainer_IsReportedBeforeGeneratingAnInvalidPartialClass()
    {
        var result = Generate(Imports + """
            public record E : Error;
            public static class Problems {
                [HttpProblem(400)] private static void Configure(ProblemBuilder<E> p) { }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, item => item.Id == "FORHTTP002");
        Assert.Empty(result.Code);
    }

    [Theory]
    [InlineData("partial record")]
    [InlineData("partial record struct")]
    [InlineData("partial struct")]
    [InlineData("partial interface")]
    public void ConfigurationMethodsInUnsupportedContainers_AreDiagnosed(string container)
    {
        var source = Imports + $$"""
            public record E : Error;
            public {{container}} Problems {
                [HttpProblem(400), AutomaticExtensions]
                private static void Configure(ProblemBuilder<E> p) { }
                [GenericProblem]
                private static void Fallback(GenericProblemBuilder p) { }
            }
            """;
        var result = Generate(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("FORHTTP002", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.DoesNotContain(CreateCompilation(source).GetDiagnostics(), item => item.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.Code);
    }

    [Theory]
    [InlineData("[UseDataField]", "private static void Configure(ProblemBuilder<E> p) { }")]
    [InlineData("[AutomaticExtensions]", "private static void Configure(ProblemBuilder<E> p) { }")]
    [InlineData("[HttpProblem(400), UseDataField]", "private static void Configure(ProblemBuilder<E> p) { }")]
    [InlineData("[GenericProblem, AutomaticExtensions]", "private static void Configure(GenericProblemBuilder p) { }")]
    public void OrphanAndIncompatibleMarkers_AreErrors(string attributes, string method)
    {
        var result = Generate(Imports + "public record E : Error; public static partial class Problems { " + attributes + method + " }");

        Assert.Contains(result.GeneratorDiagnostics, item => item.Id == "FORHTTP003");
    }

    [Fact]
    public void InvalidStatusAndDuplicateMaps_AreErrors()
    {
        var result = Generate(Imports + """
            public record E : Error;
            public static partial class Problems {
                [HttpProblem(300)] private static void BadStatus(ProblemBuilder<E> p) { }
                [HttpProblem(400)] private static void First(ProblemBuilder<E> p) { }
                [HttpProblem(422)] private static void Second(ProblemBuilder<E> p) { }
                [GenericProblem] private static void GenericA(GenericProblemBuilder p) { }
                [GenericProblem] private static void GenericB(GenericProblemBuilder p) { }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, item => item.Id == "FORHTTP004");
        Assert.Equal(2, result.GeneratorDiagnostics.Count(item => item.Id == "FORHTTP005"));
    }

    [Theory]
    [InlineData("dynamic", "object")]
    [InlineData("string?", "string")]
    [InlineData("nint", "System.IntPtr")]
    [InlineData("(int Id, int Code)", "(int, int)")]
    [InlineData("(int Id, int Code)", "System.ValueTuple<int, int>")]
    [InlineData("System.Collections.Generic.List<dynamic[]>", "System.Collections.Generic.List<object[]>")]
    [InlineData("(dynamic Value, string? Name)[]", "(object, string)[]")]
    [InlineData("Outer<dynamic>.Inner<int>", "Outer<object>.Inner<int>")]
    public void EquivalentClosedTypes_AreDiagnosedAsDuplicateMaps(string first, string second)
    {
        var source = Imports + $$"""
            public record E<T> : Error;
            public class Outer<T> { public class Inner<U>; }
            public static partial class Problems {
                [HttpProblem(400)] private static void First(ProblemBuilder<E<{{first}}>> p) { }
                [HttpProblem(422)] private static void Second(ProblemBuilder<E<{{second}}>> p) { }
            }
            """;
        var result = Generate(source);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("FORHTTP005", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.DoesNotContain(CreateCompilation(source).GetDiagnostics(), item => item.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.Code);
    }

    [Theory]
    [InlineData("int", "long")]
    [InlineData("int", "int?")]
    [InlineData("object[]", "object[,]")]
    public void DistinctClosedTypes_AreNotDuplicateMaps(string first, string second)
    {
        var result = Generate(Imports + $$"""
            public record E<T> : Error;
            public static partial class Problems {
                [HttpProblem(400)] private static void First(ProblemBuilder<E<{{first}}>> p) { }
                [HttpProblem(422)] private static void Second(ProblemBuilder<E<{{second}}>> p) { }
            }
            """);

        AssertNoErrors(result);
        Assert.Equal(2, result.Code.Split('\n').Count(line => line.Contains("catalog.Add<", StringComparison.Ordinal)));
    }

    [Fact]
    public void UnselectedDomainAssembly_IsAnActionableConfigurationError()
    {
        var result = Generate(Imports + """
            public static partial class Problems {
                [HttpProblem(404), AutomaticExtensions]
                private static void Configure(ProblemBuilder<UserNotFound> p) { }
            }
            """);

        var diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("FORHTTP006", diagnostic.Id);
        Assert.Contains("ErrorAssembly", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReservedNames_WarnAndOmitOnlyForDirectAutomaticProjection(bool data)
    {
        var result = Generate(Imports + """
            public record E : Error { public string status => "domain"; public string Status => "case matters"; }
            public static partial class Problems {
                [HttpProblem(400), AutomaticExtensions]
            """ + (data ? "[UseDataField]" : "") + """
                private static void Configure(ProblemBuilder<E> p) { }
            }
            """);

        AssertNoErrors(result);
        Assert.Equal(data ? 0 : 1, result.GeneratorDiagnostics.Count(item => item.Id == "FORHTTP007"));
        Assert.Equal(data, result.Code.Contains("error.@status;", StringComparison.Ordinal));
        Assert.Contains("error.@Status;", result.Code);
    }

    [Fact]
    public void HidingAndOverrides_EmitOneNormalAccessPerName()
    {
        var result = Generate(Imports + """
            public record Base : Error { public string Code => "base"; public virtual string Message => "base"; }
            public record Child : Base { public new string Code => "child"; public override string Message => "child"; }
            public static partial class Problems {
                [HttpProblem(400), AutomaticExtensions]
                private static void Configure(ProblemBuilder<Child> p) { }
            }
            """);

        AssertNoErrors(result);
        Assert.Single(result.Code.Split('\n'), line => line.Contains("error.@Code;", StringComparison.Ordinal));
        Assert.Single(result.Code.Split('\n'), line => line.Contains("error.@Message;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("private new string Code => \"hidden\";", true)]
    [InlineData("protected new string Code => \"hidden\";", true)]
    [InlineData("public new static string Code => \"static\";", false)]
    [InlineData("public new string Code = \"field\";", false)]
    public void PropertySelection_UsesAccessibilityAtTheGeneratedCallSite(string member, bool projected)
    {
        var result = Generate(Imports + "public record Base : Error { public string Code => \"base\"; } " +
            "public record Child : Base { " + member + " } " +
            "public static partial class Problems { [HttpProblem(400), AutomaticExtensions] " +
            "private static void Configure(ProblemBuilder<Child> p) { } }");

        AssertNoErrors(result);
        Assert.Equal(projected, result.Code.Contains("error.@Code;", StringComparison.Ordinal));
    }

    [Fact]
    public void PartialConfigurationInSeveralFiles_IsGeneratedOnlyOnce()
    {
        var result = Generate(Imports + """
            public record E : Error;
            public static partial class Problems {
                [HttpProblem(400)] private static void Configure(ProblemBuilder<E> p) { }
            }
            """, Imports + """
            public static partial class Problems {
                [GenericProblem] private static void Fallback(GenericProblemBuilder p) { }
            }
            """);

        AssertNoErrors(result);
        Assert.Equal(2, result.GeneratedCount);
    }

    [Fact]
    public void UnboxableProperties_AreDiagnosedWithoutRestrictingOrdinaryObjects()
    {
        var result = Generate(Imports + """
            public record E : Error { public System.ReadOnlySpan<int> Values => default; }
            public static partial class Problems {
                [HttpProblem(400), AutomaticExtensions] private static void Configure(ProblemBuilder<E> p) { }
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, item => item.Id == "FORHTTP008");
    }

    [Fact]
    public void EditingOnlyConfigurationBody_DoesNotChangeGeneratedSources()
    {
        var source = Imports + """
            public record E : Error;
            public static partial class Problems {
                [HttpProblem(400), AutomaticExtensions]
                private static void Configure(ProblemBuilder<E> p) => p.Title("Before");
            }
            """;
        var compilation = CreateCompilation(source);
        var options = (CSharpParseOptions)compilation.SyntaxTrees.Single().Options;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new ProblemGenerator().AsSourceGenerator() },
            parseOptions: options, driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);
        var changed = compilation.ReplaceSyntaxTree(compilation.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText(source.Replace("Before", "After", StringComparison.Ordinal), options, "Source0.cs"));
        driver = driver.RunGenerators(changed);
        var steps = driver.GetRunResult().Results.Single().TrackedSteps["CatalogSources"];

        Assert.All(steps.SelectMany(step => step.Outputs), output =>
            Assert.True(output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));
    }

    [Fact]
    public void ImplementationOutputsMayBeSkipped_WithoutHidingTheRegistrationApi()
    {
        var source = Imports + """
            public record E : Error;
            public static partial class Problems {
                [HttpProblem(400)] private static void Configure(ProblemBuilder<E> p) { }
            }
            public static class Usage {
                public static void Configure(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services) =>
                    services.AddProblems();
            }
            """;
        var compilation = CreateCompilation(source);
        var options = (CSharpParseOptions)compilation.SyntaxTrees.Single().Options;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new ProblemGenerator().AsSourceGenerator() },
            parseOptions: options, driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.Implementation));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.DoesNotContain(diagnostics, item => item.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), item => item.Severity == DiagnosticSeverity.Error);
        Assert.Single(driver.GetRunResult().Results.Single().GeneratedSources);
    }

    [Fact]
    public void PersistedSourceHints_AreBoundedAndDeterministicForLongNamespaces()
    {
        var source = Imports.Replace("namespace Consumer;", "namespace " + new string('N', 180) + ";", StringComparison.Ordinal) +
            "public record E : Error; public static partial class Problems { " +
            "[HttpProblem(400)] private static void Configure(ProblemBuilder<E> p) { } }";
        var compilation = CreateCompilation(source);
        var options = (CSharpParseOptions)compilation.SyntaxTrees.Single().Options;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new ProblemGenerator().AsSourceGenerator() }, parseOptions: options);
        var first = driver.RunGenerators(compilation).GetRunResult().Results.Single().GeneratedSources;
        var second = driver.RunGenerators(compilation).GetRunResult().Results.Single().GeneratedSources;

        Assert.Equal(first.Select(item => item.HintName), second.Select(item => item.HintName));
        Assert.All(first, item => Assert.InRange(item.HintName.Length, 1, 160));
    }

    [Theory]
    [InlineData("void Outer() { [HttpProblem(400)] static void Local(ProblemBuilder<E> p) { } }")]
    [InlineData("void Outer() { System.Action<ProblemBuilder<E>> f = [HttpProblem(400)] (ProblemBuilder<E> p) => { }; }")]
    [InlineData("[HttpProblem(400)] public Holder() { }")]
    public void AttributesOnUnsupportedMethodKinds_AreNotSilentlyIgnored(string member)
    {
        var result = Generate(Imports + "public record E : Error; public class Holder { " + member + " }");

        Assert.Contains(result.GeneratorDiagnostics, item => item.Id == "FORHTTP001");
    }

    [Fact]
    public void AssemblyMarkersOnRecords_AreReportedAsInvalidContainers()
    {
        var result = Generate(Imports + "[ErrorAssembly(typeof(UserNotFound))] public record Wrong;");

        Assert.Contains(result.GeneratorDiagnostics, item => item.Id == "FORHTTP002");
    }

    private static void AssertNoErrors(Generated result)
    {
        Assert.DoesNotContain(result.GeneratorDiagnostics, item => item.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Compilation.GetDiagnostics(), item => item.Severity == DiagnosticSeverity.Error);
    }

    private static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Concat(new[] { typeof(Error).Assembly.Location, typeof(ProblemBuilder<>).Assembly.Location, typeof(DomainError).Assembly.Location })
            .Distinct(StringComparer.Ordinal);
        var references = paths.Select(path => MetadataReference.CreateFromFile(path));
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var trees = sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, options, $"Source{index}.cs"));
        return CSharpCompilation.Create("Consumer", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static Generated Generate(params string[] sources)
    {
        var compilation = CreateCompilation(sources);
        var options = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new ProblemGenerator().AsSourceGenerator() }, parseOptions: options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var run = driver.GetRunResult();
        var generated = run.Results.SelectMany(item => item.GeneratedSources).ToArray();

        return new(output, diagnostics.ToArray(), string.Join("\n", generated.Select(item => item.SourceText.ToString())), generated.Length);
    }

    private sealed record Generated(Compilation Compilation, Diagnostic[] GeneratorDiagnostics, string Code, int GeneratedCount);
}
