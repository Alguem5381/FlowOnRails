using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analysis.Internal;

internal sealed class FlowObservation
{
    internal SyntaxNode Expression { get; }
    internal FlowValue Value { get; set; } = FlowValue.Bottom;

    internal FlowObservation(SyntaxNode expression) => Expression = expression;
}
