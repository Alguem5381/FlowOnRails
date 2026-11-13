# .NET 10 / .NET 11 compatibility — 2026-11-13

## Outcome

The four local packages pass isolated consumer checks on .NET 10 and .NET 11.
Their runtime assets remain `net10.0`; compiler tools remain `netstandard2.0`
with Roslyn 5.0.0 as the minimum dependency. No publication was performed.

Production builds use SDK 10.0.1xx. The separate compatibility entry point uses
SDK 11.0.1xx. Both policies accept servicing patches and reject prereleases.
Tool source uses explicit C# 14.0 instead of `latest`. CI and CD now run the
runtime suites and isolated package checks on both versions before publication.

## Reproduced defect and correction

An extension indexer returning a rail caused `ErrorFlowGenerator` to fail with
CS8785 and `NotSupportedException`. A direct query through the public API reproduced
the same exception in `TypeSubstitution.Apply`.

Roslyn 5.12 represents an extension block with a synthetic containing type. That
symbol rejects `WithNullableAnnotation`, even when the annotation is unchanged.
Type substitution now returns the substituted symbol directly when its annotation
already matches. Actual nullable-annotation changes retain the existing behavior.
The fix avoids changing the minimum Roslyn dependency or suppressing exceptions.

The regression runs through both the public API and the packaged generator:
an extension getter must produce `Missing`, and a separate assembly must read
that same precise error from the generated reference-assembly metadata.

## Validation

Local environment: Linux Mint 22 / Ubuntu 24.04, x64. The system installation was
left intact. SDK 11 was downloaded into a temporary directory, and its SHA-512
matched the official checksum before the compatibility runs.

| Check | Result |
| --- | --- |
| Production SDK 10.0.115 build and packaging | Passed; zero warnings/errors |
| Existing suite on runtime 10.0.15 | 1,309 passed; no failures/skips |
| Existing suite on SDK 11.0.100 / runtime 11.0.0 | 1,309 passed; no failures/skips |
| Release-selection Python tests | 6 passed |
| Four isolated NuGet consumers on .NET 10 | Passed |
| Four isolated NuGet consumers on .NET 11 | Passed |
| Four isolated .NET 11 consumers with runtime-async enabled | Passed |
| Public API on SDK 11's Roslyn 5.12.0 | 12 C# 15 method summaries and an open interface contract passed |
| C# 15 exported summaries | 11 consumer wrappers preserve their expected error sets, in both async modes |
| HTTP integration | Seven HTTP/MVC responses and the invalid-status generator diagnostic pass in each consumer mode |
| Configuration | Project XML, workflow YAML and whitespace checks passed |

The C# 15 input covers union payloads and pattern matching, closed domain error
families, collection constructor arguments including a `ref` write, labeled
break/continue, extension indexers, Task/ValueTask methods and asynchronous
Bind/MapError callbacks. A closed family does not close an interface contract.
Isolated builds treat warnings as errors and verify normal diagnostics, generated
metadata, explicit opt-out and absence of compiler-tool runtime dependencies in
application consumers.

## Artifacts and scope

The tested packages are in `artifacts/net10-net11-0.3.0`, with `SHA256SUMS`:

- `FlowOnRails.0.3.0.nupkg`
- `FlowOnRails.Analysis.0.3.0.nupkg`
- `FlowOnRails.Analyzers.0.3.0.nupkg`
- `FlowOnRails.AspNetCore.0.1.0.nupkg`

The SDK 11 API fixture intentionally loads the compiler's Roslyn assemblies;
merely executing Roslyn 5.0 on a newer runtime would not test C# 15 input.
The ordinary .NET 10 API fixture retains the normal NuGet dependency.

These results cover the implemented scenarios, not every valid C# 15 program or
IDE integration. Existing conservative analysis limits remain in effect. Preview
memory-safety rules and performance measurements are outside this change. The
library itself is still compiled for .NET 10 without runtime-native async; the
opt-in check applies to consuming .NET 11 code. Hosted CI/CD execution and NuGet
publication were not triggered locally. The default restore was returned to the
repository's .NET 10 policy after the checks.

See [compatibility](../compatibility.md) for repeatable commands and source links.
