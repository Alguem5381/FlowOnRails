; Unshipped analyzer release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
FORFLOW001 | FlowAnalysis | Info | Experimental error flow summary with bounded concrete calls and open interface contracts, enabled through FlowOnRailsEnableFlowAnalysis
FORFLOW002 | FlowAnalysis | Info | Experimental error flow at method calls before caller recovery, enabled through FlowOnRailsEnableFlowAnalysis
FORFLOW003 | FlowAnalysis | Hidden | Offers cases for known errors without removing the switch fallback or suppressing compiler diagnostics
