# Package verification

These executable fixtures intentionally remain outside `FlowOnRails.slnx`.
Their unpublished packages must be packed before restore. Build the solution and
pack Core, Analysis and AspNetCore into one clean directory;
see [the publishing guide](../../docs/publishing.md) for the exact commands.

```sh
python3 scripts/verify-packages.py ./artifacts/packages
python3 scripts/verify-packages.py ./artifacts/packages --framework net10.0 --framework net11.0
python3 scripts/verify-packages.py ./artifacts/packages --framework net11.0 --runtime-async
python3 -m unittest discover -s tests/packaging -p 'test_*.py'
# Only after the required base version is publicly available:
python3 scripts/verify-packages.py ./artifacts/packages --published-base
```

The verifier inspects actual archives and copies each consumer outside the
repository, so central package management, project references to the library and
existing build output cannot mask packaging problems. Each run uses a fresh cache.

The default remains .NET 10. Repeat `--framework` to verify both versions with
their own SDK policies. The .NET 11 producer additionally exercises C# 15 unions,
closed error families, collection arguments, extension indexers and labeled jumps;
its consumer asserts precise exported summaries. The API consumer uses SDK 11's
Roslyn assemblies to query those constructs directly. `--runtime-async` recompiles
only .NET 11 consumers with runtime-native async enabled. Both SDKs are needed
for the combined verification. See [compatibility](../../docs/compatibility.md).

| Fixture | References | Verification |
|---|---|---|
| CoreConsumer | Base package | Successful values and original failures |
| AspNetCoreConsumer | ASP.NET package | Transitive base, generated registration, seven HTTP/MVC responses and FORHTTP004 for invalid status |
| AnalyzerConsumer | Base package, in both producer and consumer projects | Explicit opt-in, exported reference-assembly summaries, FORFLOW001/002/003, default off and explicit false |
| AnalysisConsumer | Analysis API and base packages | Public API with concrete methods and open interface contracts |

Application fixtures must not receive Roslyn or the analysis engine as runtime
dependencies. The API fixture is a development tool and deliberately uses Roslyn.

The default mode restores FlowOnRails packages from the local feed. The API fixture
also restores Roslyn dependencies from nuget.org. Supply
`--tooling-dependency-feed /path/to/nupkg-feed` to use another dependency source,
including an offline archive feed. No library project references are used.

`--published-base` runs the ASP.NET and base analyzer fixtures with source mapping that
excludes the local base package and permits that dependency only from nuget.org.
CD runs both modes for releases of the ASP.NET integration, before authentication.
This mode is expected to fail until their configured base dependency is published.

The Python unit tests exercise release selection, including all three package tags,
legacy base tags, version mismatches, prereleases and malformed tags.
