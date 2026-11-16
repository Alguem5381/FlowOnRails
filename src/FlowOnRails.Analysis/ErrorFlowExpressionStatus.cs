namespace FlowOnRails.Analysis;

/// <summary>Whether a value at a requested expression could be analyzed.</summary>
public enum ErrorFlowExpressionStatus
{
    /// <summary>The expression was evaluated and has a conservative summary.</summary>
    Analyzed = 0,
    /// <summary>Supported control flow does not evaluate the expression.</summary>
    Unreachable = 1,
    /// <summary>The expression's type, body or evaluation could not be observed.</summary>
    Unsupported = 2
}
