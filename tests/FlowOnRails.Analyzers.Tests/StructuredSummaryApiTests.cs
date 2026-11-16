using FlowOnRails.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    [Fact]
    public void TryApi_SeparatesMissingLibraryUnsupportedMethodsAndSuccessfulQueries()
    {
        Assert.False(ErrorFlowAnalysis.TryCreate(CSharpCompilation.Create("Plain"), out var absent));
        Assert.Null(absent);
        Assert.Throws<ArgumentNullException>(() => ErrorFlowAnalysis.TryCreate(null!, out _));

        const string source = """
            using FlowOnRails;
            public sealed record Failure : Error;
            public static class Api
            {
                public static RailFor<int> Read() => new Failure();
                public static string Text() => "text";
            }
            """;
        var compilation = ApiCompilation(source);
        Assert.True(ErrorFlowAnalysis.TryCreate(compilation, out var analysis));
        Assert.False(analysis.TryAnalyzeMethod(null, out var nullSummary));
        Assert.Null(nullSummary);
        Assert.False(analysis.TryAnalyzeMethod(ApiMethod(compilation, "Api", "Text"), out var unsupported));
        Assert.Null(unsupported);
        Assert.False(analysis.TryAnalyzeMethod(ApiMethod(ApiCompilation(source), "Api", "Read"), out var foreign));
        Assert.Null(foreign);
        Assert.True(analysis.TryAnalyzeMethod(ApiMethod(compilation, "Api", "Read"), out var summary));
        Assert.Equal("Failure", Assert.Single(summary.KnownErrorTypes).Name);
        Assert.Equal(ErrorFlowSummaryOrigin.SourceBody, summary.Origin);
        Assert.Empty(summary.LimitationDetails);
        Assert.Throws<OperationCanceledException>(() => analysis.TryAnalyzeMethod(
            ApiMethod(compilation, "Api", "Read"), out _, new CancellationToken(true)));
    }

    [Fact]
    public void StructuredSummary_DistinguishesRootOriginFromNestedCallLimits()
    {
        const string source = """
            using System.Runtime.CompilerServices;
            using FlowOnRails;
            public sealed record Failure : Error;
            public interface IReader { [ResultsWith<Failure>] RailFor<int> Read(); }
            public static class Api
            {
                public static RailFor<int> Read(IReader reader) => reader.Read();
                [MethodImpl(MethodImplOptions.InternalCall)]
                public static extern RailFor<int> External();
            }
            """;
        var compilation = ApiCompilation(source);
        var analysis = new ErrorFlowAnalysis(compilation);
        Assert.True(analysis.TryAnalyzeMethod(ApiMethod(compilation, "IReader", "Read"), out var contract));
        Assert.Equal(ErrorFlowSummaryOrigin.OpenContract, contract.Origin);
        Assert.True(contract.HasUnknownErrors);
        Assert.Equal(ErrorFlowLimitationCode.OpenDispatch, Assert.Single(contract.LimitationDetails).Code);

        var body = analysis.AnalyzeMethod(ApiMethod(compilation, "Api", "Read"));
        Assert.Equal(ErrorFlowSummaryOrigin.SourceBody, body.Origin);
        Assert.Equal(ErrorFlowLimitationCode.OpenDispatch, Assert.Single(body.LimitationDetails).Code);
        Assert.Equal(body.LimitationDetails.Select(detail => detail.Message), body.Limitations);

        Assert.True(analysis.TryAnalyzeMethod(ApiMethod(compilation, "Api", "External"), out var unavailable));
        Assert.Equal(ErrorFlowSummaryOrigin.OpenContract, unavailable.Origin);
        Assert.Equal(ErrorFlowLimitationCode.BodyUnavailable, Assert.Single(unavailable.LimitationDetails).Code);

        var reference = EmitProducer(source, true, out _);
        var consumer = ApiCompilation("class Consumer { }", reference);
        var exported = new ErrorFlowAnalysis(consumer).AnalyzeMethod(ApiMethod(consumer, "Api", "Read"));
        Assert.Equal(ErrorFlowSummaryOrigin.AssemblyMetadata, exported.Origin);
        Assert.Equal(ErrorFlowLimitationCode.OpenDispatch, Assert.Single(exported.LimitationDetails).Code);
    }

    [Fact]
    public void MissingMetadata_StillSucceedsAsAnOpenQueryWithAStructuredReason()
    {
        var reference = EmitProducer(ProducerSource, false, out _);
        var consumer = ApiCompilation("class Consumer { }", reference);
        var analysis = new ErrorFlowAnalysis(consumer);

        Assert.True(analysis.TryAnalyzeMethod(ApiMethod(consumer, "Producer.Api", "Direct"), out var summary));
        Assert.Equal(ErrorFlowSummaryOrigin.OpenContract, summary.Origin);
        Assert.True(summary.HasUnknownErrors);
        Assert.Equal(ErrorFlowLimitationCode.MetadataMissing, Assert.Single(summary.LimitationDetails).Code);
    }

    [Theory]
    [InlineData(999, "anything", false, ErrorFlowLimitationCode.MetadataUnsupportedVersion)]
    [InlineData(3, "not base64", false, ErrorFlowLimitationCode.MetadataInvalid)]
    [InlineData(3, "anything", true, ErrorFlowLimitationCode.MetadataConflict)]
    public void UnreadableMetadata_ProvidesDistinctReasonsWithoutParsingMessages(
        int version, string payload, bool duplicate, ErrorFlowLimitationCode expectedCode)
    {
        var attribute = $"[assembly: FlowOnRails.FlowAnalysisSummaryAttribute({version}, \"M:Producer.Api.Read~FlowOnRails.RailFor{{System.Int32}}\", \"{payload}\")]";
        var conflictingAttribute = $"[assembly: FlowOnRails.FlowAnalysisSummaryAttribute({version}, \"M:Producer.Api.Read~FlowOnRails.RailFor{{System.Int32}}\", \"{payload}-conflicting\")]";
        var source = $$"""
            {{attribute}}
            {{(duplicate ? conflictingAttribute : "")}}
            namespace Producer;
            public static class Api { public static FlowOnRails.RailFor<int> Read() => FlowOnRails.Rail.Ok(1); }
            """;
        var reference = EmitProducer(source, false, out _);
        var compilation = ApiCompilation("class Consumer { }", reference);
        var analysis = new ErrorFlowAnalysis(compilation);

        Assert.True(analysis.TryAnalyzeMethod(ApiMethod(compilation, "Producer.Api", "Read"), out var summary));
        Assert.Equal(ErrorFlowSummaryOrigin.OpenContract, summary.Origin);
        Assert.True(summary.HasUnknownErrors);
        Assert.Equal(expectedCode, Assert.Single(summary.LimitationDetails).Code);
    }

    [Fact]
    public void StructuredLimits_SurviveGuardedMetadataAndReferenceOutputsWithoutLeakingUnselectedReasons()
    {
        const string producer = """
            using FlowOnRails;
            namespace Producer;
            public sealed record Failure : Error;
            public interface IReader { RailFor<int> Read(); }
            public static class Api
            {
                public static RailFor<int> Choose(bool known, IReader reader) => known ? new Failure() : reader.Read();
                public static void Fill(bool known, IReader reader, out RailFor<int> value) => value = Choose(known, reader);
            }
            """;
        var reference = EmitProducer(producer, true, out var generated);
        Assert.Contains("FlowAnalysisSummaryAttribute(3,", generated);
        var consumer = ApiCompilation("""
            using FlowOnRails;
            using Producer;
            public static class Consumer
            {
                public static RailFor<int> Known(IReader reader) => Api.Choose(true, reader);
                public static RailFor<int> Unknown(IReader reader) => Api.Choose(false, reader);
                public static RailFor<int> Output(IReader reader) { Api.Fill(false, reader, out var value); return value; }
            }
            """, reference);
        var analysis = new ErrorFlowAnalysis(consumer);
        var known = analysis.AnalyzeMethod(ApiMethod(consumer, "Consumer", "Known"));
        Assert.Empty(known.LimitationDetails);
        Assert.False(known.HasUnknownErrors);
        Assert.Equal("Producer.Failure", Assert.Single(known.KnownErrorTypes).ToDisplayString());

        foreach (var methodName in new[] { "Unknown", "Output" })
        {
            var summary = analysis.AnalyzeMethod(ApiMethod(consumer, "Consumer", methodName));
            Assert.Equal(ErrorFlowSummaryOrigin.SourceBody, summary.Origin);
            Assert.True(summary.HasUnknownErrors);
            Assert.Equal(ErrorFlowLimitationCode.OpenDispatch, Assert.Single(summary.LimitationDetails).Code);
        }
    }

    [Fact]
    public void InaccessibleSummaryTypes_ExposeTheirStructuredReason()
    {
        var compilation = ApiCompilation("class Consumer { }", EmitProducer(ProducerSource, true, out _));
        var summary = new ErrorFlowAnalysis(compilation).AnalyzeMethod(ApiMethod(compilation, "Producer.Api", "Hidden"));

        Assert.Equal(ErrorFlowSummaryOrigin.AssemblyMetadata, summary.Origin);
        Assert.True(summary.HasUnknownErrors);
        Assert.Contains(summary.LimitationDetails, detail => detail.Code == ErrorFlowLimitationCode.MetadataTypeUnavailable);
        Assert.Contains(summary.KnownErrorTypes, type => type.Name == "SecondFailure");
    }

    [Fact]
    public void AnalysisBudgetLimit_HasAStableReasonLocallyAndAfterExport()
    {
        var declarations = string.Join("\n", Enumerable.Range(0, 40).Select(methodIndex =>
            $"public static RailFor<int> Read{methodIndex}() => Read{methodIndex + 1}();"));
        var source = $$"""
            using FlowOnRails;
            public static class Api
            {
                {{declarations}}
                public static RailFor<int> Read40() => Rail.Ok(1);
            }
            """;
        var local = ApiCompilation(source);
        var consumer = ApiCompilation("class Consumer { }", EmitProducer(source, true, out _));

        foreach (var compilation in new[] { local, consumer })
        {
            var summary = new ErrorFlowAnalysis(compilation).AnalyzeMethod(ApiMethod(compilation, "Api", "Read0"));
            Assert.True(summary.HasUnknownErrors);
            Assert.Contains(summary.LimitationDetails, detail => detail.Code == ErrorFlowLimitationCode.AnalysisBudgetExceeded);
        }
    }

    [Theory]
    [InlineData(1, 0, ErrorFlowLimitationCode.Unclassified)]
    [InlineData(2, 0, ErrorFlowLimitationCode.Unclassified)]
    [InlineData(3, 1, ErrorFlowLimitationCode.OpenDispatch)]
    [InlineData(3, 999999, ErrorFlowLimitationCode.Unclassified)]
    public void MetadataVersions_PreserveMessagesWithoutInferringCodesFromText(
        int version, int storedCode, ErrorFlowLimitationCode expectedCode)
    {
        // Deliberately reuse a current explanation in legacy records, and a
        // different language in v3. Only the serialized ID determines the code.
        var message = version < 3 ? "Interface or virtual dispatch may return additional errors" : "Motivo traduzido";
        var payload = SummaryWithOneLimit(version, storedCode, message);
        var reference = EmitProducer($$"""
            [assembly: FlowOnRails.FlowAnalysisSummaryAttribute({{version}}, "M:Producer.Api.Read~FlowOnRails.RailFor{System.Int32}", "{{payload}}")]
            namespace Producer;
            public static class Api { public static FlowOnRails.RailFor<int> Read() => FlowOnRails.Rail.Ok(1); }
            """, false, out _);
        var compilation = ApiCompilation("class Consumer { }", reference);
        var summary = new ErrorFlowAnalysis(compilation).AnalyzeMethod(ApiMethod(compilation, "Producer.Api", "Read"));

        Assert.Equal(ErrorFlowSummaryOrigin.AssemblyMetadata, summary.Origin);
        Assert.True(summary.MaySucceed);
        Assert.False(summary.HasUnknownErrors);
        var detail = Assert.Single(summary.LimitationDetails);
        Assert.Equal(expectedCode, detail.Code);
        Assert.Equal(message, detail.Message);
        Assert.Equal(message, Assert.Single(summary.Limitations));
    }

    [Fact]
    public void LegacyVersionTwo_GuardsSelectTheirOwnUnclassifiedReasons()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteSummary(true, true, "Aggregate legacy reason");
        writer.Write(2); // Two Boolean guard alternatives in the v2 format.
        WriteValue(4, false, false, 1);
        WriteSummary(true, false, "Known legacy path");
        WriteValue(4, false, false, 0);
        WriteSummary(false, true, "Other legacy path");
        writer.Flush();
        var payload = Convert.ToBase64String(stream.ToArray());
        var reference = EmitProducer($$"""
            [assembly: FlowOnRails.FlowAnalysisSummaryAttribute(2, "M:Producer.Api.Choose(System.Boolean)~FlowOnRails.RailFor{System.Int32}", "{{payload}}")]
            namespace Producer;
            public static class Api { public static FlowOnRails.RailFor<int> Choose(bool known) => FlowOnRails.Rail.Ok(1); }
            """, false, out _);
        var compilation = ApiCompilation("""
            public static class Consumer { public static FlowOnRails.RailFor<int> Read() => Producer.Api.Choose(true); }
            """, reference);
        var analysis = new ErrorFlowAnalysis(compilation);
        var summary = analysis.AnalyzeMethod(ApiMethod(compilation, "Consumer", "Read"));

        Assert.True(summary.MaySucceed);
        Assert.False(summary.MayBeInvalid);
        Assert.False(summary.HasUnknownErrors);
        Assert.Equal(ErrorFlowSummaryOrigin.SourceBody, summary.Origin);
        Assert.Equal(ErrorFlowLimitationCode.Unclassified, Assert.Single(summary.LimitationDetails).Code);
        Assert.Equal("Known legacy path", Assert.Single(summary.Limitations));

        var aggregate = analysis.AnalyzeMethod(ApiMethod(compilation, "Producer.Api", "Choose"));
        Assert.Equal(ErrorFlowSummaryOrigin.AssemblyMetadata, aggregate.Origin);
        Assert.True(aggregate.MaySucceed);
        Assert.True(aggregate.MayBeInvalid);
        Assert.Equal(2, aggregate.LimitationDetails.Length);
        Assert.All(aggregate.LimitationDetails, detail => Assert.Equal(ErrorFlowLimitationCode.Unclassified, detail.Code));

        void WriteSummary(bool success, bool invalid, string message)
        {
            WriteValue(3, success, invalid, -1);
            writer.Write(false);
            writer.Write(1);
            writer.Write(message);
            writer.Write(0); // No parameter outputs.
        }

        void WriteValue(byte kind, bool success, bool invalid, int boolean)
        {
            writer.Write(kind);
            writer.Write(success);
            writer.Write(invalid);
            writer.Write(false);
            writer.Write(boolean);
            writer.Write(-1);
            writer.Write((byte)0);
            writer.Write(0);
        }
    }

    private static string SummaryWithOneLimit(int version, int code, string message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)3); // Rail success; no errors, dependency or nullable value.
        writer.Write(true);
        writer.Write(false);
        writer.Write(false);
        writer.Write(-1);
        writer.Write(-1);
        if (version >= 2)
            writer.Write((byte)0);
        writer.Write(0);
        writer.Write(false);
        writer.Write(1);
        if (version >= 3)
            writer.Write(code);
        writer.Write(message);
        if (version >= 2)
        {
            writer.Write(0); // Parameter outputs.
            writer.Write(0); // Guarded variants.
        }
        writer.Flush();

        return Convert.ToBase64String(stream.ToArray());
    }
}
