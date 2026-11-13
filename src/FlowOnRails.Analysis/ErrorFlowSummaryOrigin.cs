namespace FlowOnRails.Analysis;

/// <summary>How the root method query was resolved, independent of the origins of nested calls.</summary>
public enum ErrorFlowSummaryOrigin
{
    /// <summary>The method body in the current compilation was interpreted.</summary>
    SourceBody = 0,
    /// <summary>An exported summary from a referenced assembly was read.</summary>
    AssemblyMetadata = 1,
    /// <summary>An open contract was used for unresolved dispatch or unavailable analysis.</summary>
    OpenContract = 2
}
