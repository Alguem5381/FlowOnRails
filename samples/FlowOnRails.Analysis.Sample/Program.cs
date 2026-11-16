using FlowOnRails.Analysis.Sample;

foreach (var scenario in Enum.GetValues<Scenario>())
{
    foreach (var useFallback in new[] { false, true })
    {
        var result = ErrorFlowStudy.Study(scenario, useFallback);
        var text = result.TryUnwrap(out var value, out var error)
            ? $"Success({value})"
            : error.GetType().Name;

        Console.WriteLine($"{scenario}, fallback={useFallback}: {text}");
    }
}
