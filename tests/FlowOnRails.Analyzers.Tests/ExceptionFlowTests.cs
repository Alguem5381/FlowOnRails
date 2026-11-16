using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    [Theory]
    [InlineData("try { return Concrete(); } catch (System.IO.IOException) { return new A(); }", "A, B")]
    [InlineData("try { throw new InvalidOperationException(); } catch { return new A(); }", "A")]
    [InlineData("try { _ = Rail.Ok(42).Tap(value => { throw new Exception(); }); return new A(); } catch { return new B(); }", "B")]
    [InlineData("try { throw new InvalidOperationException(); } catch { throw; }", "")]
    [InlineData("RailFor<int> result = new A(); try { return result; } catch { return new B(); }", "A")]
    [InlineData("try { return new A(); } finally { _ = new B(); }", "A")]
    [InlineData("try { return new A(); } finally { }", "A")]
    [InlineData("try { return new A(); } finally { _ = Rail.Ok(1).Tap(value => { throw new Exception(); }); }", "")]
    [InlineData("try { throw new Exception(); } catch { _ = Rail.Ok(1).Tap(value => { throw new Exception(); }); return new A(); }", "")]
    [InlineData("try { return new A(); } catch { return new B(); } finally { throw new Exception(); }", "")]
    [InlineData("RailFor<int> result = default; try { result.TryUnwrap(out _, out _); return new A(); } catch { return new B(); }", "B")]
    [InlineData("RailFor<int> result = new A(); try { return result; } finally { result = new B(); }", "A")]
    [InlineData("RailFor<int> result = new A(); try { result = new B(); } finally { result = new C(); } return result;", "C")]
    [InlineData("try { return new A(); } finally { throw new InvalidOperationException(); }", "")]
    [InlineData("try { return new A(); } finally { if (flag) throw new InvalidOperationException(); }", "A")]
    [InlineData("try { return new A(); } finally { while (true) { } }", "")]
    [InlineData("RailFor<int> result = new A(); try { try { result = new B(); } finally { result = new C(); } } finally { result = new A(); } return result;", "A")]
    [InlineData("RailFor<int> result = new A(); while (flag) { try { result = new B(); break; } finally { result = new C(); } } return result;", "A, C")]
    [InlineData("RailFor<int> result = new A(); while (flag) { try { flag = false; continue; } finally { result = new B(); } } return result;", "A, B")]
    [InlineData("using var stream = new System.IO.MemoryStream(); return new A();", "A")]
    public async Task ExceptionFlow_PreservesOnlyNormalReturnValues(string body, string expectedErrors)
    {
        var diagnostic = Assert.Single(await Analyze(body));

        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Equal("False", diagnostic.Properties["MayBeInvalid"]);
    }

    [Fact]
    public async Task ExceptionFlow_UnwindsBeforeEnteringAnOuterHandler()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> result = new A();
            try
            {
                try { throw new InvalidOperationException(); }
                finally { result = new B(); }
            }
            catch { return result; }
            """));

        // Constructing B may itself throw before assignment, retaining A.
        Assert.Equal("A, B", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task ExceptionFlow_AnInnerCatchAllStopsTheOriginalExceptionSearch()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> first = new A();
            RailFor<int> second = new B();
            try
            {
                try { throw new InvalidOperationException(); }
                catch { return first; }
            }
            catch { return second; }
            """));

        Assert.Equal("A", diagnostic.Properties["ErrorTypes"]);
    }

    [Fact]
    public async Task ExceptionFlow_ThrowsInFinallyAbandonThePendingReturn()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> first = new A();
            RailFor<int> second = new B();
            try
            {
                try { return first; }
                finally { throw new InvalidOperationException(); }
            }
            catch { return second; }
            """));

        Assert.Equal("B", diagnostic.Properties["ErrorTypes"]);
    }

    [Fact]
    public async Task ExceptionFlow_RethrowsSearchOutsideTheCurrentHandler()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> result = new C();
            try
            {
                try { throw new InvalidOperationException(); }
                catch { throw; }
            }
            catch { return result; }
            """));

        Assert.Equal("C", diagnostic.Properties["ErrorTypes"]);
    }

    [Fact]
    public async Task ExceptionFlow_TracksStateBeforeAndAfterRefEffects()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> result = new A();
            try { Mutate(ref result); }
            catch { return result; }
            return new C();
            """));

        Assert.Contains("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Contains("C", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task ExceptionFlow_ObservesAssignmentsBeforeSubsequentThrowingOperations()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> result = new A();
            try
            {
                result = new B();
                _ = External();
                result = new C();
            }
            catch { return result; }
            return Rail.Ok(42);
            """));

        Assert.Equal("A, B", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("True", diagnostic.Properties["MaySucceed"]);
    }

    [Fact]
    public async Task ExceptionFlow_PropertyPatternCallbacksCannotPreserveStaleCatchValues()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> result = new A();
            Action callback = () => result = new B();
            var probe = new Probe(callback);
            result = new A();
            try
            {
                if (probe is { Ready: true }) return Rail.Ok(42);
                return result;
            }
            catch { return result; }
            """, declarations: """
            private sealed class Probe(Action callback)
            {
                public bool Ready { get { callback(); throw new Exception(); } }
            }
            """));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("effects", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task ExceptionFlow_ConversionCallbacksCannotPreserveStaleCatchValues()
    {
        var diagnostic = Assert.Single(await Analyze("""
            RailFor<int> result = new A();
            Action callback = () => result = new B();
            var probe = new Probe(callback);
            result = new A();
            try { _ = (int)probe; }
            catch { return result; }
            return result;
            """, declarations: """
            private sealed class Probe(Action callback)
            {
                public static explicit operator int(Probe probe)
                {
                    probe.Run();
                    throw new Exception();
                }
                private void Run() => callback();
            }
            """));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Theory]
    [InlineData("try { return new A(); } catch (Exception) when (flag) { return new B(); }")]
    [InlineData("RailFor<int> result = new A(); try { throw new Exception(); } catch (Exception) when (Change(ref result)) { return result; }")]
    public async Task ExceptionFlow_FiltersRemainExplicitlyOpen(string body)
    {
        var diagnostic = Assert.Single(await Analyze(body, declarations: """
            private static bool Change(ref RailFor<int> result) { result = new B(); return true; }
            """));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("Exception filter effects", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task ExceptionFlow_FilterFallbackKeepsKnownBodyReturnTypes()
    {
        var diagnostic = Assert.Single(await Analyze("""
            try { return new A(); }
            catch (Exception) when (flag) { return new B(); }
            catch { return new C(); }
            """));

        Assert.Contains("A", diagnostic.Properties["ErrorTypes"]);
        Assert.Contains("B", diagnostic.Properties["ErrorTypes"]);
        Assert.Contains("C", diagnostic.Properties["ErrorTypes"]);
    }

    [Theory]
    [InlineData("try { await Task.FromException<int>(new InvalidOperationException()); return new A(); } catch { return new B(); }", "B")]
    [InlineData("try { return new A(); } finally { await Task.Yield(); }", "A")]
    [InlineData("try { return new A(); } finally { await Task.FromException<int>(new InvalidOperationException()); }", "")]
    public async Task ExceptionFlow_HandlesAsyncFaultsAndFinalizers(string body, string expectedErrors)
    {
        var diagnostic = Assert.Single(await Analyze(body, returnType: "Task<RailFor<int>>", isAsync: true));

        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task ExceptionFlow_DeepCleanupNestingOpensTheSummaryWithinTheSharedLimit()
    {
        var cleanup = "flag = !flag;";

        for (var regionDepth = 0; regionDepth < 35; regionDepth++)
            cleanup = "try { flag = !flag; } finally { " + cleanup + " }";

        var diagnostic = Assert.Single(await Analyze("try { return new A(); } finally { " + cleanup + " }"));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("nesting limit exceeded", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task ExceptionFlow_PropagatesThroughConcreteMethods()
    {
        var diagnostic = Assert.Single(await Analyze("return Read();", declarations: """
            private static RailFor<int> Read()
            {
                try { return new A(); }
                catch { return new B(); }
            }
            """));

        Assert.Equal("A, B", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task ExceptionFlow_ExportsThePostFinallySummaryToAnotherAssembly()
    {
        var producer = ProducerSource.Replace("public static RailFor<int> Direct() => new FirstFailure();", """
            public static RailFor<int> Direct()
            {
                RailFor<int> result = new FirstFailure();
                try { result = new FirstFailure(); }
                finally { result = new SecondFailure(); }
                return result;
            }
            """);
        var reference = EmitProducer(producer, true, out _);
        var diagnostic = Assert.Single(await AnalyzeConsumer("return Api.Direct();", "RailFor<int>", false, reference));

        Assert.Equal("Producer.SecondFailure", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task ExceptionFlow_ReportsCallsInHandlersAndFinalizers()
    {
        const string source = """
            using System;
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Study
            {
                public static void Subject()
                {
                    try { throw new Exception(); }
                    catch { _ = Rail.Fail<int>(new Failure()); }
                    finally { _ = Rail.Fail(new Failure()); }
                }
            }
            """;
        var diagnostics = await AnalyzeSource(source, true, false, null);
        var calls = diagnostics.Where(diagnostic => diagnostic.Id == "FORFLOW002").ToArray();

        Assert.Equal(2, calls.Length);
        Assert.All(calls, diagnostic => Assert.Equal("Failure", diagnostic.Properties["ErrorTypes"]));
    }
}
