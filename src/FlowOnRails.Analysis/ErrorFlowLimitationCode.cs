namespace FlowOnRails.Analysis;

/// <summary>Stable reasons for conservative error-flow analysis.</summary>
/// <remarks>Numeric values are preserved in assembly summaries. Messages are presentation only.</remarks>
public enum ErrorFlowLimitationCode
{
    /// <summary>A legacy or unrecognized reason has no supported classification.</summary>
    Unclassified = 0,
    /// <summary>An interface or unresolved virtual contract permits additional errors.</summary>
    OpenDispatch = 1,
    /// <summary>A method body is unavailable in the current compilation.</summary>
    BodyUnavailable = 2,
    /// <summary>No exported summary was found for a referenced method.</summary>
    MetadataMissing = 3,
    /// <summary>Exported summary data is malformed or incompatible with the method.</summary>
    MetadataInvalid = 4,
    /// <summary>The exported summary uses an unsupported schema version.</summary>
    MetadataUnsupportedVersion = 5,
    /// <summary>Multiple exported summaries conflict for the same method.</summary>
    MetadataConflict = 6,
    /// <summary>A type referenced by a summary is unavailable or inaccessible.</summary>
    MetadataTypeUnavailable = 7,
    /// <summary>A depth, control-flow or call-context budget was exceeded.</summary>
    AnalysisBudgetExceeded = 8,
    /// <summary>Recursive approximation did not stabilize within its budget.</summary>
    RecursionDidNotConverge = 9,
    /// <summary>Reference storage or aliased argument effects cannot be tracked precisely.</summary>
    ReferenceEffectsUntracked = 10,
    /// <summary>Callback or captured local-function effects require conservative state.</summary>
    CallbackEffectsUntracked = 11,
    /// <summary>Captured state across asynchronous execution is not tracked precisely.</summary>
    AsyncCapturedStateUntracked = 12,
    /// <summary>An escaping closure environment cannot be retained precisely.</summary>
    EscapingClosure = 13,
    /// <summary>Storage outside supported local cells cannot be tracked precisely.</summary>
    StorageUntracked = 14,
    /// <summary>A value operation or library operator is unsupported.</summary>
    UnsupportedOperation = 15,
    /// <summary>A conversion cannot be tracked precisely.</summary>
    UnsupportedConversion = 16,
    /// <summary>Possible exception paths are approximated conservatively.</summary>
    ExceptionFlowApproximation = 17,
    /// <summary>Exception-filter effects cannot be tracked precisely.</summary>
    ExceptionFilterEffectsUntracked = 18,
    /// <summary>A returned value could not be represented in the supported domain.</summary>
    ReturnValueUnrepresented = 19,
    /// <summary>Flow at a call site is unavailable in its current context.</summary>
    CallSiteUnavailable = 20,
    /// <summary>A delegate has unresolved possible targets.</summary>
    DelegateTargetUnknown = 21,
    /// <summary>Deconstruction destinations require conservative values.</summary>
    DeconstructionUntracked = 22,
    /// <summary>A reference alias points outside tracked storage.</summary>
    ReferenceAliasUntracked = 23
}
