namespace FlowOnRails;

/// <summary>
/// Stores a versioned error-flow summary generated at compilation time.
/// This metadata is consumed by tooling; use <see cref="ResultsWithAttribute"/>
/// to declare known errors in a source contract.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class FlowAnalysisSummaryAttribute : Attribute
{
    /// <summary>Gets the summary format version.</summary>
    public int SchemaVersion { get; }

    /// <summary>Gets the original method's documentation identifier.</summary>
    public string MethodId { get; }

    /// <summary>Gets the encoded summary.</summary>
    public string Payload { get; }

    /// <summary>Initializes the generated summary metadata.</summary>
    /// <param name="schemaVersion">The summary format version.</param>
    /// <param name="methodId">The original method identifier.</param>
    /// <param name="payload">The encoded summary.</param>
    public FlowAnalysisSummaryAttribute(int schemaVersion, string methodId, string payload) =>
        (SchemaVersion, MethodId, Payload) = (schemaVersion, methodId, payload);
}
