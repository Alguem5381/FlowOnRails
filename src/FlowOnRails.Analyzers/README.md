# Experimental error flow analysis

This is an experimental analysis of `Rail`, `RailFor<T>` and their Task/ValueTask
results using Roslyn operations and the control flow graph. It follows concrete
method bodies in the current compilation and reports **Info** diagnostics:
`FORFLOW001` on method declarations and `FORFLOW002` on method calls. Interface declarations report an open contract,
including interfaces with a default implementation. `FORFLOW003` is hidden and
offers a code fix to add known error cases to a switch.

FlowOnRails 0.3.0 includes the compiler analyzer, summary generator, shared
engine and editor fixes in its base package. Set
`<FlowOnRailsEnableFlowAnalysis>true</FlowOnRailsEnableFlowAnalysis>` in each
project that should analyze or export summaries. Analysis is off by default.
The analyzer assets do not add Roslyn to the application runtime. The reusable
API is available separately as `FlowOnRails.Analysis`.

## Try the study

Open `samples/FlowOnRails.Analysis.Sample/Study.cs` in an IDE that supports Roslyn
analyzers and enable informational/suggestion diagnostics. `Study` combines the
cases below; the test suite also checks them in isolation, including negative
cases where an error must remain in the returned set.

```bash
dotnet run --project samples/FlowOnRails.Analysis.Sample
dotnet test tests/FlowOnRails.Analyzers.Tests
```

The executable prints the actual result for each scenario with and without a
fallback. Build output normally hides informational diagnostics; inspect them in
the IDE or request a compiler SARIF error log.

The sample explicitly references the analyzer and opts in:

```xml
<PropertyGroup>
  <FlowOnRailsEnableFlowAnalysis>true</FlowOnRailsEnableFlowAnalysis>
</PropertyGroup>
<ItemGroup>
  <CompilerVisibleProperty Include="FlowOnRailsEnableFlowAnalysis" />
  <ProjectReference Include="../../src/FlowOnRails.Analyzers/FlowOnRails.Analyzers.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

A direct project reference requires the property above. The NuGet package supplies
the property and CompilerVisibleProperty automatically unless explicitly disabled.
The two diagnostics can be configured independently in an editorconfig file:

```ini
[*.cs]
dotnet_diagnostic.FORFLOW001.severity = suggestion
dotnet_diagnostic.FORFLOW002.severity = suggestion
```

Call Info is attached to the invoked method's name, including generic and conditional
member calls. It describes the result at the call, before subsequent recovery in
the consumer. Calls are also reported in methods returning other types, constructors
and accessors. Supported call results use the caller's CFG values and join repeated
loop visits into one diagnostic. Nested concrete method frames do not publish
duplicate diagnostics or specialize the callee's declaration to one caller.
Ordinary library `Ok` calls have no call Info. `Ok<TValue>` with an `Error`
TValue has a specific Info explaining that the error is a successful payload; it
does not contribute to the failure set. `Fail` calls retain their error summaries.
Calls whose flow is unavailable, including unsupported regions, field/property
initializers and unevaluated callback bodies, report an open contract with an explicit limitation.

Messages use a namespace tree with compacted unbranched prefixes, one type per line,
and separate paragraphs for uncertainty and analysis limits. The open base `Error`
family is represented by the open-result explanation rather than a redundant type
entry; exclusions are retained separately. Generic arguments and nested type names
keep their identities. Messages distinguish no identified returned errors from an
open source with no known concrete error types. The same plain-text message is used
by IDE diagnostics and compiler reports; rendering of line breaks depends on the host.

## Editor actions

The separate `src/FlowOnRails.CodeFixes` project contains the editor providers and
references Roslyn Workspaces 5.0.0. Load its output alongside the analyzer in a
Roslyn-capable editor (the study sample references both projects as analyzers).
The compiler analyzer and generator themselves have no Workspaces dependency.
`EditorActions.cs` in the sample provides examples for all three behaviors.

On a concrete implementation's **FORFLOW001**, choose **Declare known errors on
interface contracts (ResultsWith)**. The fix edits every matching, editable source
interface, including another project in the solution. Each error must resolve to
the same type in the contract's compilation and be accessible from that interface.
The fix adds repeatable single-type attributes, skips already declared types/families,
and never adds project references. Implementation-only and private types are omitted;
metadata-only contracts cannot be edited. Generic attribute arguments cannot contain
type parameters, so those candidates are omitted. Contracts remain open.

On a **switch** keyword, **FORFLOW003** offers **Add cases for known errors (keep
other cases)**. This is a hidden diagnostic, with no warning or claim of completeness.
It works for statements and expressions, including top-level statements, using the
error's possibilities at that point in supported flow. Existing unconditional type
cases are preserved; a guarded case does not normally cover all values of its type.
New cases follow existing specific cases and precede an unconditional fallback;
derived types precede newly added base types. Each generated body throws
`NotImplementedException` as a visible placeholder for user-written handling.
All existing fallbacks remain, including for open interface contracts. Complex
negated/composite existing patterns are deliberately left without an action.
No compiler exhaustiveness warning is suppressed.

Both actions are individual fixes, without Fix All, to avoid competing edits to a
shared contract or changing several switches without reviewing their placeholders.
They require the same opt-in as the analyzer. IDE discovery and action rendering
are provided by the host; command-line builds emit diagnostics but cannot apply fixes.

### ReSharper for VS Code

In the tested ReSharper 2026.3 installation, the switch action was not offered for
the default hidden diagnostic. Enable its Info presentation in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.FORFLOW003.severity = suggestion
```

This adds an informational hint on the switch keyword, with the same optional fix;
it does not introduce a warning or assert that the error set is exhaustive.
The analyzer's default remains hidden for hosts that support fixes on hidden rules.

After replacing the experimental DLLs, reload the IDE window so its Roslyn worker
loads the new analyzer and code-fix assemblies. A running worker can retain older
providers while the editor already shows newer diagnostics. During the lab test,
the worker logged `Cached diagnostic not found` until the window was reloaded.

The ResultsWith action appears on the concrete implementation's method name, on
its existing FORFLOW001 Info. There is no separate missing-attribute warning on
the interface. Test both actions with Alt+Enter; once their changes are applied,
they disappear until another eligible error or missing switch case is introduced.

## Current scope

- Direct failures, implicit error conversions, successful results, typed-to-untyped
  rail conversion, local assignments and copies, and overwritten/discarded errors.
  A rail used as a successful payload has its own independent state: an inner
  failure does not make the outer result fail.
- `if`, conditional expressions and switch paths represented by the CFG, plus a
  bounded fixed point for loops.
- Type/declaration patterns, negation, `and`/`or` patterns, null tests and constants
  already supplied by Roslyn. Property/relational patterns remain conservative.
- `TryUnwrap`, saved extraction booleans, `IsSuccess` and `IsFailure`.
- `try`, `catch`, rethrows and `finally`, including nested regions, cleanup lowered
  from `using`, and `break`/`continue` edges that leave a protected region. Pending
  returns are evaluated before finally and counted only if cleanup completes normally.
  See the exception-flow policy below.
- Models for synchronous `Map`, `Bind`, `Check`, `Ensure`, `MapError`, `Recover`,
  `RecoverWith`, `Tap`, `TapIf`, `TapError` and `Match`, with inline lambdas analyzed
  inside the same method. Callback failures are only considered on applicable paths.
- `Task.FromResult`, `ValueTask.FromResult`, ValueTask result constructors, `await`,
  `ConfigureAwait`, `AsTask`, and the library's instance-style async pipelines.
  Async lambdas use the same flow interpreter. Faults and cancellation do not
  automatically introduce domain errors.
- Exact constructed error types are distinguished from a parameter's possible
  subtypes. Pattern tests can restrict a family without assuming it is sealed.
- Concrete methods returning rails or errors in the current compilation, including
  generic methods and methods on constructed generic types. Error constructors,
  casts and patterns use the constructed type arguments.
  Arguments initialize a fresh method frame, so forwarding a known error preserves
  its types and a known boolean can select a callee's return path. Graphs are cached;
  returned values are analyzed in their call context instead of being shared between
  calls with different arguments.

## Calls and mutable cells

Known delegates retain lambda/method-group targets through assignments, local copies,
branches and arguments to another concrete method. Each target is interpreted from
an independent pre-call state, and normal completions are joined. Unknown targets,
multicast transformations and more than 16 possible targets retain an open alternative.
Declaring a lambda or local function does not execute its body. Local functions and
supported callbacks use their declaring CFG; concrete getters and indexers use their
available bodies. Interface and unresolved virtual dispatch remain open.

Call frames copy symbol storage without CFG capture IDs. Supported captured writes
and ref/out changes are copied back from normal exits after finally. Ref arguments
can share one cell within a body. Local ref aliases and ref reassignments preserve
storage identity; joining possible aliases uses weak writes. A conditional reference
whose storage cannot be recovered stays unknown, and a write through it invalidates
potential cells. Heap fields/arrays and ref returns are not modeled as local cells.
Exceptional calls conservatively seed handler states for possible partial writes.
Escaping closure environments and captured state across async suspension remain open.
Ignoring a faulted async result does not terminate a synchronous continuation.

Ordinary recursive call cycles are solved by monotonically growing return summaries
for the same abstract inputs, starting at bottom. A closed result is published only
when all participating contexts stabilize, within 16 iterations, depth 32, at most
256 call contexts and the shared 4096-block budget. Exhaustion remains explicitly open.
Recursive captured/ref effects still invalidate state: return-value convergence is
not a proof that those mutable cells have stabilized.

## Summaries between assemblies

The same opt-in enables `ErrorFlowGenerator` in the producer. It emits assembly-level
`FlowAnalysisSummaryAttribute` entries, with a format version, the original method's
documentation identifier and an encoded abstract result. It does not require partial
methods or change method signatures. The metadata survives reference-assembly emission.

IDE project references may expose source symbols through `CompilationReference`.
Their syntax still belongs to another compilation: the consumer reads exported
summaries and never tries to open those trees in its own semantic model.

Consumers read these entries when a concrete method body is unavailable. The format
preserves known error types, open families and exclusions, success/invalid/null states,
awaited results, and supported parameter dependencies. Generic type
arguments are represented structurally and substituted in the constructed consumer call.
For example, a generic forwarding method can preserve the consumer's concrete errors
instead of reusing the producer's unknown input.

Version 2 exports bounded guarded alternatives for boolean inputs, rail states and
error families tested by supported patterns. It also preserves input-error to
failure conversions, rail-error extraction, and normal ref/out parameter outputs.
Consumers intersect each input guard with the actual argument and join every
compatible result. Guard partitions are complete, with at most 16 alternatives;
a split that would exceed that limit is left unsplit. Graphs above 128 blocks use
only the broad base summary. This is an abstract partition, not a sample of executions.
Version 3 adds stable limitation codes alongside their presentation messages.
Versions 1 and 2 remain readable; text-only legacy reasons are exposed as
`Unclassified`. Unknown reason IDs within version 3 also retain their messages
as `Unclassified`. These formats do not serialize arbitrary method bodies or
every conditional relationship between an argument, a return and an output cell.
Private/inaccessible or missing error types widen the affected result while preserving
accessible known types. Missing, conflicting, malformed or unknown-version metadata
leaves the result open. A supported producer must opt in; absence of metadata never
means that an external method cannot fail.

The generator sees its input compilation, so summaries depending on output from another
generator may remain open. The analyzer uses source bodies available in the final
compilation and never treats its own generated manifest as a replacement for those bodies.
Disabling the Info diagnostic does not disable generation when the feature flag is enabled.

## Open interface contracts

```csharp
public interface IUserReader
{
    [ResultsWith<UserNotFound, AccessDenied>]
    [ResultsWith<UnavailableService>]
    RailFor<User> Read(int id);
}
```

`ResultsWith` has generic arities from one to sixteen, each constrained to `Error`,
and is repeatable. It documents known possibilities rather than an exhaustive list.
The analyzer reads these attributes from source and referenced assemblies. The
interface and calls through it retain an open remainder even when an implementation
is visible or the interface supplies a default body. Unresolved virtual dispatch
follows the same policy. A concrete sealed override may use its available body.

An open source does not permanently make a caller open: replacing all returned
failures with `MapError(error => new PublicFailure())` can close the caller's result.

Tracking a value does not require that the method return it. Only the errors that
can reach a normal return as a rail failure contribute to its summary. Constructing
an error or returning an Error as a successful value does not introduce a failure.

## Exception flow

`MethodFlowAnalysis.ControlFlow.cs` adds the exceptional successors missing from
ordinary CFG branches. Potentially throwing operations seed handler states before
and after evaluation, retaining possible partial ref/out or callback effects.
Exception types thrown by arbitrary calls are not summarized: applicable catch paths
are considered conservatively, and Info reports **Possible exception paths are
considered conservatively**. This may add known error types from handlers that never
run in practice; it does not by itself introduce an unknown domain error type.
Unhandled exceptions never become `FlowOnRails.Error` automatically.

The interpreter executes `ControlFlowBranch.FinallyRegions` in their supplied order
before continuing a regular edge or accepting a return. Each finally execution has
its own input states, separate from other pending transfers. Assigning to a local
in finally affects later reads but does not replace an already evaluated return.
A finally that always throws, faults when awaited or loops forever prevents the
pending normal return. Throws/rethrows search enclosing handlers, unwind inner
finally blocks, and cannot re-enter their own catch. An unconditional catch-all
stops the original exception's search; a new exception from its body searches outward.
The same rules apply to concrete callees and generated assembly summaries.

Exception filters remain deliberately approximate. A reached filter group retains
known body-return types, widens handler inputs and the method summary, and reports
**Exception filter effects are not fully tracked**. Filter expressions can change
locals before unwinding, even when rejected; their absence from the interpreted
handler path is never treated as proof of a closed result. Inline filter calls whose
flow is unavailable keep the ordinary open call-site diagnostic.

All exception-region work shares the 4096-block budget with ordinary flow and called
methods. Nested finally execution is also bounded; exceeding either limit opens the
summary explicitly. `ExceptionStudy.cs` in the sample demonstrates normal catch
mapping, pending return values and finally assignments.

## Two independent limitations

**Undetermined conditions** cause both paths to be considered. The reported types
may include errors from infeasible paths; this does not by itself introduce unknown
error types. The Info explicitly mentions this loss of precision.

**Unknown error sources** mean other concrete error types may be returned. Interface
dispatch, unavailable bodies and unsupported call signatures remain open. Supported
concrete bodies in the current compilation are followed using the caller's arguments.
An unknown result remains open until the returned error is sufficiently narrowed
or completely replaced, for example by `MapError(error => new PublicFailure())`.

The diagnostics also have structured properties: `MethodName`, `TargetKind`
(`Declaration` or `Invocation`), `ErrorTypes`, `HasUnknownErrors`,
`HasUnresolvedConditions`, `MaySucceed`, `MayBeInvalid`, `SuccessValueIsError` and `Limitations`.
`ErrorTypes` retains qualified names and the open base family for tooling; the tree
is a presentation change and does not alter assembly summaries.

## Deliberately deferred

- Complete relational summaries of arbitrary expressions and relationships between
  returned booleans and ref/out cells. Guarded metadata supports a bounded subset;
  aliasing between reference arguments requires a local body to preserve write order.
- Mutable state across recursive cycles, escaping closure environment identities,
  async captured state, arbitrary/multicast delegate targets, element tracking in
  collections, heap aliases, ref returns, custom awaiters and precise successful payloads.
- Full expression evaluation, deconstruction precision and precise exception/filter
  effects. Unknown calls, constructors and getters can invoke escaped callbacks;
  invalidation discards stale facts. Unsupported conversions/operators, recursive
  patterns, dynamic operations and static field initialization also invalidate
  tracked state when callbacks can mutate it. Ordinary captured assignments,
  local-function bodies and concrete ref/out methods are now interpreted.
- Correlations between independently copied values and precise successful payloads.
  These may make the summary broader than necessary.
- Switch exhaustiveness diagnostics and suppression. This prototype never suppresses
  compiler diagnostics or treats an open interface as a closed contract.

The tooling retains Roslyn 5.0.0 as its minimum compiler dependency. Package
fixtures validate .NET 10 and .NET 11 consumers, including the SDK 11 compiler's
C# 15 unions, closed error families, labeled jumps, collection arguments and
extension indexers. The public API is also exercised with SDK 11's Roslyn 5.12.0.
These scenarios preserve the existing analysis limits; the engine is not a
complete interpreter for every valid C# program. See
[compatibility and build selection](https://github.com/Alguem5381/FlowOnRails/blob/develop/docs/compatibility.md).

## Integration and package guides

See [the usage guide](https://github.com/Alguem5381/FlowOnRails/blob/develop/docs/error-flow.md),
[the public API](https://github.com/Alguem5381/FlowOnRails/blob/develop/src/FlowOnRails.Analysis/README.md)
and [satellite integration](https://github.com/Alguem5381/FlowOnRails/blob/develop/docs/design/analysis-integration.md).
These preview packages have not been published by this preparation step.
