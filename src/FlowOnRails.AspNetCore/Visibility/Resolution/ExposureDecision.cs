namespace FlowOnRails.AspNetCore.Visibility;

/// <summary>Describes visibility independently of HTTP mappings or serialized properties.</summary>
public sealed class ExposureDecision
{
    /// <summary>The effective behavior for the concrete error.</summary>
    public RuleBehavior Behavior { get; }

    /// <summary>
    /// Authorized projection contracts, nearest first. A translator must choose the first
    /// candidate with a registered mapping and must project that contract, not the concrete error.
    /// An empty list requires a generic response; missing mappings also require a generic response.
    /// </summary>
    public IReadOnlyList<Type> TranslationCandidates { get; }

    internal ExposureDecision(RuleBehavior behavior, IEnumerable<Type> candidates)
    {
        Behavior = behavior;
        TranslationCandidates = Array.AsReadOnly([.. candidates]);
    }
}
