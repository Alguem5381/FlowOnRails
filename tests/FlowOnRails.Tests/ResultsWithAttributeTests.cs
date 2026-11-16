namespace FlowOnRails.Tests;

public class ResultsWithAttributeTests
{
    public static IEnumerable<object[]> GenericArities =>
        Enumerable.Range(1, 16).Select(arity => new object[] { arity });

    [Theory]
    [MemberData(nameof(GenericArities))]
    public void EveryArity_RequiresErrorTypesAndExposesThemInOrder(int arity)
    {
        var attributeDefinition = typeof(Error).Assembly.GetType($"FlowOnRails.ResultsWithAttribute`{arity}")!;
        var errorTypes = Enumerable.Repeat(typeof(FirstFailure), arity).ToArray();
        errorTypes[^1] = typeof(LastFailure);
        var attributeType = attributeDefinition.MakeGenericType(errorTypes);
        var attribute = Assert.IsAssignableFrom<ResultsWithAttribute>(Activator.CreateInstance(attributeType));

        Assert.Equal(errorTypes, attribute.ErrorTypes);

        for (var typeParameterIndex = 0; typeParameterIndex < arity; typeParameterIndex++)
        {
            var invalidTypes = (Type[])errorTypes.Clone();
            invalidTypes[typeParameterIndex] = typeof(string);

            Assert.Throws<ArgumentException>(() => attributeDefinition.MakeGenericType(invalidTypes));
        }
    }

    private sealed record FirstFailure : Error;
    private sealed record LastFailure : Error;
}
