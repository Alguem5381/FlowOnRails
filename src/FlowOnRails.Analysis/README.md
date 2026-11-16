# FlowOnRails.Analysis

Preview Roslyn API for generators and analyzers that need FlowOnRails error-flow
summaries. This package is for **tool authors**, including satellite generators.
Applications install `FlowOnRails`, which includes opt-in error-flow diagnostics.

It targets `netstandard2.0` and references Roslyn C# 5.0.0. The compilation being
inspected must reference FlowOnRails. The analysis assembly has no runtime reference
to FlowOnRails and does not need Workspaces, an IDE or a diagnostic severity setting.

The packaged API is tested with Roslyn 5.0.0 on .NET 10 and the SDK 11 compiler's
Roslyn 5.12.0 on .NET 11, including C# 15 input. Satellite tools must supply a
Roslyn host that understands their input language; targeting .NET 11 alone does
not update the tool's parser. See
[compatibility](https://github.com/Alguem5381/FlowOnRails/blob/develop/docs/compatibility.md).

## Query a method

```csharp
using System.Linq;
using FlowOnRails.Analysis;
using Microsoft.CodeAnalysis;

if (!ErrorFlowAnalysis.TryCreate(compilation, out var analysis))
    return;

if (!analysis.TryAnalyzeMethod(methodSymbol, out var summary, cancellationToken))
    return;

foreach (var errorType in summary.KnownErrorTypes)
    RegisterKnownError(errorType);

if (summary.HasUnknownErrors)
    PreserveUnknownErrorFallback();
```

`compilation`, `methodSymbol`, `RegisterKnownError` and `PreserveUnknownErrorFallback`
are supplied by the tool. Reuse one context within an immutable compilation; create
another for a changed compilation. Concurrent queries on the same context are
supported. Query methods returning Rail, RailFor, Error or supported Task/ValueTask
wrappers. A method symbol must come from that compilation or its references.

`TryCreate` returns false and a null context when required FlowOnRails types are
absent or ambiguous. A null compilation throws `ArgumentNullException`.
`TryAnalyzeMethod` returns false and a null summary for null/foreign symbols and
unsupported returns. A successful query has a non-null summary even for open
contracts, missing bodies or invalid metadata. Cancellation propagates as
`OperationCanceledException`, and unexpected exceptions are not suppressed.
The nullable annotations support ordinary `if` guards without null-forgiving casts.

The constructor and `AnalyzeMethod` remain available for callers that already know
their inputs are supported; invalid inputs throw. `CanAnalyze` checks support
without interpreting the body. The Try methods are conveniences, not a different
analysis or an assertion that a successful query has a closed error set.

The query uses unknown parameter inputs. It follows concrete bodies in the local
compilation, exported metadata in referenced assemblies, and open ResultsWith
contracts for interfaces or unresolved virtual dispatch. A missing body or missing
metadata gives an explicitly open summary, never an empty certificate.

Local functions and lambdas can also be passed to `AnalyzeMethod` or
`TryAnalyzeMethod` using their Roslyn `IMethodSymbol`. These are independent
entry points: parameters and captured inputs start unknown. When captured inputs
are present, `CapturedInputUnknown` explains the missing calling context. This
does not change how known calls within an analyzed body specialize their arguments
and supported captured state.

## Query expressions within a body

Use `AnalyzeExpressions` when the value of interest is passed to another API,
recovered later, or lives in a method returning an unrelated type such as `IResult`.

```csharp
var results = analysis.AnalyzeExpressions(
    bodySymbol, valueExpressions, cancellationToken);

foreach (var result in results)
{
    if (result.Status != ErrorFlowExpressionStatus.Analyzed)
        continue;

    var summary = result.Summary!;

    foreach (var errorType in summary.KnownErrorTypes)
        RegisterKnownError(errorType);

    if (summary.HasUnknownErrors)
        PreserveUnknownErrorFallback();
}
```

`bodySymbol` is a source `IMethodSymbol` for a method, constructor, local function
or lambda in the compilation. `valueExpressions` is an
`IEnumerable<ExpressionSyntax>` containing expressions from that same executable
body. A nested lambda or local function is a separate body, even when declared
inside the supplied method. Use its own symbol to query expressions inside it.
For a lambda, `IAnonymousFunctionOperation.Symbol` provides that symbol.

The body's return type is unrestricted. A point's supported value type is Rail,
RailFor or Error, optionally wrapped in Task/ValueTask. Source bodies of virtual
methods are interpreted as those implementations; this does not close their
dispatch contracts when called elsewhere or queried with `AnalyzeMethod`.

One body analysis collects all requested points. Results preserve input order and
duplicates. Each value joins the possibilities observed at that point across
branches and repeated loop visits. For example, querying a value before recovery
can report failures while querying the recovered value reports success. Calls at
these points use supported argument specialization and referenced-assembly metadata.

| Status | Contract |
| --- | --- |
| `Analyzed` | `Summary` is non-null and conservative. Unknown alternatives and no normal result remain possible states within that summary. |
| `Unreachable` | Supported flow did not evaluate the expression; `Summary` is null. |
| `Unsupported` | The type, body or evaluation could not be observed; `Summary` is null and `LimitationDetails` explains why. |

A point with an unsupported type does not disable supported points. If an
unobserved point cannot be established as unreachable because the body has
limitations, it is `Unsupported`. Exhausting the shared analysis budget also
opens the summaries of observed points instead of presenting a partial traversal
as exhaustive. Write-only destinations are unsupported value queries.

Error alternatives are specific to the requested point. Conditions and limitations
conservatively describe the containing body and can mention other operations;
they are not precise dependency traces for that individual value.

Null/foreign symbols, null/foreign expressions and expressions belonging to another
body throw argument exceptions. Cancellation propagates. An empty sequence returns
an empty result. The API does not execute application code or accept hypothetical
input values supplied by the tool.

## Result contract

| Member | Meaning |
| --- | --- |
| `ErrorCases` | Exact types or open families, including excluded subfamilies |
| `KnownErrorTypes` | Named cases/family bounds, without the base Error marker |
| `HasUnknownErrors` | Some case includes unnamed subtypes; preserve a fallback |
| `MayProduceResult` | Supported flow may produce a result after unwrapping awaitables |
| `MaySucceed` | A successful rail may be produced |
| `MayBeInvalid` | An uninitialized rail may be produced |
| `MayReturnNull` | An Error-returning method may return null |
| `HasUnresolvedConditions` | Undetermined conditions may broaden the list |
| `Limitations` | Human-readable explanations, not stable machine identifiers |
| `LimitationDetails` | Stable `Code` plus presentation `Message` for each limitation |
| `Origin` | Resolution of the root query: `SourceBody`, `AssemblyMetadata` or `OpenContract` |

Each `ErrorFlowCase` exposes `Type`, `IsExact` and `ExcludedTypes`. A named family
is not a finite enumeration of all its subclasses. Use `SymbolEqualityComparer`
to compare symbols within the compilation. Summary objects expose immutable arrays;
they and their symbols must not be cached across different compilations.

## Structured reasons and origin

Use `ErrorFlowLimitationCode` for programmatic decisions. For example:

```csharp
if (summary.LimitationDetails.Any(detail =>
        detail.Code == ErrorFlowLimitationCode.MetadataMissing))
    SuggestEnablingSummaryGeneration();
```

Reasons distinguish missing/invalid/unsupported/conflicting metadata, unavailable
bodies or types, open dispatch, budgets and unsupported effects/operations.
Each reason is assigned where the limitation is detected; no message parsing is
used. `Limitations` remains a distinct string view for existing presentation code.
Codes explain precision; `HasUnknownErrors` still governs unknown alternatives.

`Origin` describes the root query only. A local body can incorporate external
summaries and propagate their reasons while retaining `SourceBody`. Successful
metadata reads have `AssemblyMetadata`; unresolved dispatch and unavailable/invalid
analysis fall back to `OpenContract`. It is not a provenance graph of every error.

The writer emits metadata schema 3 with reason IDs and messages. The reader also
accepts schemas 1 and 2; their stored text-only limitations use `Unclassified`.
Unrecognized reason IDs within schema 3 also use `Unclassified` and retain their
message. Unsupported schema versions give an open result with
`MetadataUnsupportedVersion`. Schema versions are independent of NuGet versions.

This preview surface intentionally exposes no mutable interpreter state, CFG cells,
binary metadata codec, contextual call-site arguments or editor diagnostic messages.
Internal implementation types are not supported API.

## Embedding in a satellite generator

Reference this package with `PrivateAssets="all"` in the generator project. Bundle
`FlowOnRails.Analysis.dll` beside the satellite generator DLL under
`analyzers/dotnet/cs` in its application-facing package. Do not add the analysis API
as an application runtime dependency or bundle Roslyn host assemblies.

Generators cannot consume another generator's output from the same generator run.
Call this API for local source; it reads already-compiled manifests for external
assemblies. The producing projects need the analyzer/generator package installed
to export those manifests.

See the repository guides [Error-flow usage](https://github.com/Alguem5381/FlowOnRails/blob/develop/docs/error-flow.md)
and [Satellite integration](https://github.com/Alguem5381/FlowOnRails/blob/develop/docs/design/analysis-integration.md).

The API and metadata remain preview features. The independent audit and its
corrections are recorded under the repository’s `docs/reviews` directory.
An absence of unknown cases describes supported analysis, not a mathematical proof
of exhaustive runtime behavior. Never use it to suppress compiler exhaustiveness
warnings or remove general error handling.
