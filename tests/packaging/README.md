# Package verification

These executable fixtures intentionally remain outside `FlowOnRails.slnx`.
Their packages must be created before restore; including them in the solution
would try to restore the unpublished satellite during the initial build.

From the repository root, build the solution and pack both production projects
into a clean `artifacts/packages` directory, then run:

```sh
python3 scripts/verify-packages.py ./artifacts/packages
python3 scripts/verify-packages.py ./artifacts/packages --published-base
python3 -m unittest discover -s tests/packaging -p 'test_*.py'
```

The verifier inspects the actual NuGet archives and copies each consumer outside
the repository, so central package management, project references and existing
build output cannot mask packaging problems. Each run uses a fresh package cache.

`CoreConsumer` references only the base package and checks successful values and
original failures. `AspNetCoreConsumer` references only the satellite; the base
is restored transitively and the generator must supply its registration method.
It checks seven real HTTP/MVC responses, including automatic/manual extensions,
null values, hidden errors, no-content success and MVC XML formatting. A subsequent
invalid build must report `FORHTTP004` from the packaged generator. Both consumers
also check that Roslyn is absent from their runtime dependencies.

The default mode uses only the local feed. `--published-base` tests the satellite
with NuGet source mapping that excludes the local base package and permits its
dependency to come only from nuget.org. Satellite CD runs both modes before login.

The Python unit tests exercise package selection from release tags, including
legacy base tags, satellite tags, version mismatches, prereleases and malformed tags.
