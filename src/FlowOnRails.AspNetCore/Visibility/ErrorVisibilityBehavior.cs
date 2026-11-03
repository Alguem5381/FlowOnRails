namespace FlowOnRails.AspNetCore.Visibility;

/// <summary>Describes which error contract may be used for translation.</summary>
public enum ErrorVisibilityBehavior
{
    /// <summary>Use the generic response without projecting error data.</summary>
    GenericProblem,
    /// <summary>Permit translation using the concrete error type's contract.</summary>
    TranslateEachType,
    /// <summary>Use the nearest authorized ancestor for which a translation exists.</summary>
    NearestAncestorMapping
}
