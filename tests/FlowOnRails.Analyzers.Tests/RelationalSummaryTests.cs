namespace FlowOnRails.Analyzers.Tests;

public partial class ErrorFlowAnalyzerTests
{
    private const string RelationalProducer = """
        using FlowOnRails;
        namespace Producer;
        public sealed record FirstFailure : Error;
        public sealed record SecondFailure : Error;
        public static class Api
        {
            public static RailFor<int> Choose(bool first) => first ? new FirstFailure() : new SecondFailure();
            public static RailFor<int> Fail(Error error) => Rail.Fail<int>(error);
            public static RailFor<int> Replace(Error error) => error is FirstFailure ? new SecondFailure() : Rail.Fail<int>(error);
            public static RailFor<int> KeepFailure(RailFor<int> input)
            {
                if (input.TryUnwrap(out var value, out var error)) return Rail.Ok(value);
                return Rail.Fail<int>(error);
            }
            public static void Assign(out RailFor<int> result) => result = new FirstFailure();
            public static void AssignWhen(ref RailFor<int> result, bool replace)
            {
                if (replace) result = new SecondFailure();
            }
            public static void Copy(Error error, out RailFor<int> result) => result = Rail.Fail<int>(error);
            public static void AliasOrder(ref RailFor<int> first, ref RailFor<int> second)
            {
                second = new SecondFailure();
                first = new FirstFailure();
            }
            public static RailFor<int> Read => new FirstFailure();
            public static RailFor<int> Recursive(bool stop) => stop ? new FirstFailure() : Recursive(stop);
        }
        public sealed class Reader<TValue>
        {
            public RailFor<int> Read => new FirstFailure();
            public RailFor<int> this[bool first] => Api.Choose(first);
        }
        """;

    [Theory]
    [InlineData("return Api.Choose(true);", "Producer.FirstFailure")]
    [InlineData("return Api.Choose(false);", "Producer.SecondFailure")]
    [InlineData("return Api.Choose(flag);", "Producer.FirstFailure, Producer.SecondFailure")]
    [InlineData("return Api.Fail(new FirstFailure());", "Producer.FirstFailure")]
    [InlineData("return Api.Replace(new FirstFailure());", "Producer.SecondFailure")]
    [InlineData("return Api.Replace(new SecondFailure());", "Producer.SecondFailure")]
    [InlineData("return Api.KeepFailure(Rail.Fail<int>(new FirstFailure()));", "Producer.FirstFailure")]
    [InlineData("return Api.KeepFailure(Rail.Ok(42));", "")]
    [InlineData("Api.Assign(out var result); return result;", "Producer.FirstFailure")]
    [InlineData("RailFor<int> result = new FirstFailure(); Api.AssignWhen(ref result, false); return result;", "Producer.FirstFailure")]
    [InlineData("RailFor<int> result = new FirstFailure(); Api.AssignWhen(ref result, true); return result;", "Producer.SecondFailure")]
    [InlineData("Api.Copy(new SecondFailure(), out var result); return result;", "Producer.SecondFailure")]
    [InlineData("return Api.Read;", "Producer.FirstFailure")]
    [InlineData("return new Reader<int>().Read;", "Producer.FirstFailure")]
    [InlineData("return new Reader<int>()[true];", "Producer.FirstFailure")]
    [InlineData("return Api.Recursive(flag);", "Producer.FirstFailure")]
    public async Task RelationalMetadata_PreservesSupportedInputsAndOutputs(string body, string expectedErrors)
    {
        var reference = EmitProducer(RelationalProducer, true, out var generated);
        var diagnostic = Assert.Single(await AnalyzeConsumer(body, "RailFor<int>", false, reference));

        Assert.Contains("FlowAnalysisSummaryAttribute(3,", generated);
        Assert.Equal(expectedErrors, diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
    }

    [Fact]
    public async Task RelationalMetadata_DoesNotAssumeIndependentAliasedOutputs()
    {
        var reference = EmitProducer(RelationalProducer, true, out _);
        var diagnostic = Assert.Single(await AnalyzeConsumer("""
            RailFor<int> result = new FirstFailure();
            Api.AliasOrder(ref result, ref result);
            return result;
            """, "RailFor<int>", false, reference));

        Assert.Equal("True", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Contains("write order", diagnostic.Properties["Limitations"]);
    }

    [Fact]
    public async Task LegacyVersionOneSummaries_ContinueToWork()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)3); // Version-one Rail value with success and no errors.
        writer.Write(true);
        writer.Write(false);
        writer.Write(false);
        writer.Write(-1);
        writer.Write(-1);
        writer.Write(0);
        writer.Write(false);
        writer.Write(0);
        writer.Flush();
        var payload = Convert.ToBase64String(stream.ToArray());
        var reference = EmitProducer($$"""
            using FlowOnRails;
            [assembly: FlowAnalysisSummaryAttribute(1, "M:Producer.Api.Read~FlowOnRails.RailFor{System.Int32}", "{{payload}}")]
            namespace Producer;
            public static class Api { public static RailFor<int> Read() => Rail.Ok(42); }
            """, false, out _);
        var diagnostic = Assert.Single(await AnalyzeConsumer("return Api.Read();", "RailFor<int>", false, reference));

        Assert.Equal("", diagnostic.Properties["ErrorTypes"]);
        Assert.Equal("False", diagnostic.Properties["HasUnknownErrors"]);
        Assert.Equal("True", diagnostic.Properties["MaySucceed"]);
    }
}
