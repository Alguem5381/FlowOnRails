using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace FlowOnRails.Analysis.Internal;

internal enum ConditionKind
{
    Pattern,
    RailSuccess
}

internal sealed class FlowCondition
{
    internal ConditionKind Kind { get; }
    internal Slot? Subject { get; }
    internal FlowValue Value { get; }
    internal IPatternOperation? Pattern { get; }
    internal ITypeSymbol? TestedType { get; }
    internal Slot? OutError { get; }
    internal bool Negated { get; }

    internal FlowCondition(
        ConditionKind kind,
        Slot? subject,
        FlowValue value,
        IPatternOperation? pattern = null,
        ITypeSymbol? testedType = null,
        Slot? outError = null,
        bool negated = false
    )
    {
        Kind = kind;
        Subject = subject;
        Value = value;
        Pattern = pattern;
        TestedType = testedType;
        OutError = outError;
        Negated = negated;
    }

    internal FlowCondition Not() => new(Kind, Subject, Value, Pattern, TestedType, OutError, !Negated);

    internal bool DependsOn(Slot slot) => Subject?.Equals(slot) == true || OutError?.Equals(slot) == true;

    internal bool SameAs(FlowCondition? other) =>
        other is not null
        && Kind == other.Kind
        && Subject.Equals(other.Subject)
        && OutError.Equals(other.OutError)
        && Negated == other.Negated
        && ReferenceEquals(Pattern, other.Pattern)
        && SymbolEqualityComparer.Default.Equals(TestedType, other.TestedType)
        && Value.SameAs(other.Value);
}
