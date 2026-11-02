namespace FlowOnRails.PublicApi.Tests;

public class CompositionLawTests
{
    private sealed record DomainError(string Message) : Error;

    public static TheoryData<int, int, int> Cases
    {
        get
        {
            var cases = new TheoryData<int, int, int>();
            for (var state = 0; state < 3; state++)
            for (var first = 0; first < 3; first++)
            for (var second = 0; second < 3; second++)
                cases.Add(state, first, second);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BindAndMap_ObeyIdentityAndCompositionForPureCallbacks(int state, int first, int second)
    {
        var error = new DomainError("source");
        var firstError = new DomainError("first");
        var secondError = new DomainError("second");
        var source = state switch
        {
            0 => Rail.Ok(0),
            1 => Rail.Ok(7),
            _ => Rail.Fail<int>(error)
        };
        RailFor<int> F(int value) => first switch
        {
            0 => Rail.Ok(value + 1),
            1 => Rail.Ok(value * 2),
            _ => Rail.Fail<int>(firstError)
        };
        RailFor<string> G(int value) => second switch
        {
            0 => Rail.Ok(value.ToString()),
            1 => Rail.Ok($"value:{value}"),
            _ => Rail.Fail<string>(secondError)
        };

        Assert.Equal(F(7), Rail.Ok(7).Bind(F));
        Assert.Equal(source, source.Bind(value => Rail.Ok(value)));
        Assert.Equal(source.Bind(F).Bind(G), source.Bind(value => F(value).Bind(G)));
        Assert.Equal(source, source.Map(value => value));
        Assert.Equal(source.Map(value => value + 1).Map(value => value.ToString()),
            source.Map(value => (value + 1).ToString()));

        Rail plain = source;
        Assert.Equal(plain, plain.Bind(() => Rail.Ok()));
        Assert.Equal(plain.Bind(() => F(7)).Bind(G), plain.Bind(() => F(7).Bind(G)));
    }

    [Fact]
    public void ReferenceValues_RemainNonNullAfterSuccessfulUnwrapAndRecovery()
    {
        var recovered = Rail.Fail<string>(new DomainError("missing")).Recover(_ => "fallback");
        if (!recovered.TryUnwrap(out var value, out var error))
            throw new InvalidOperationException(error.ToString());
        // This assembly treats nullable warnings as errors.
        Assert.Equal(8, value.Length);
        Assert.Throws<ArgumentNullException>(() => recovered.Map<string>(_ => null!));
        Assert.Throws<ArgumentNullException>(() =>
            Rail.Fail<string>(new DomainError("missing")).Recover(_ => null!));
    }
}
