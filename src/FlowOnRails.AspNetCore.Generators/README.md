# FlowOnRails.AspNetCore.Generators

Incremental Roslyn generator for the exact HTTP mappings in FlowOnRails.AspNetCore.
The same tooling assembly includes `EndpointMappingAnalyzer`, which checks known
exact endpoint errors against statically resolved visibility and registered maps.
Targets netstandard2.0 and uses Roslyn 5.0 to support the solution's .NET 10/C# 14
consumer syntax. It is bundled under `analyzers/dotnet/cs` in the
`FlowOnRails.AspNetCore` NuGet package; no separate generator package is needed.
When developing from source, reference it as an analyzer, not as a runtime assembly.
Also reference `FlowOnRails.Analysis` as an analyzer dependency when using project
references. Both assemblies must be available to the analyzer loader.

The generator finds attributed configuration classes, validates methods and static
metadata, resolves explicitly mapped closed contracts from selected assemblies and
emits typed registrations/projectors. It does not emit references to unmapped errors
or scan every type in those assemblies. Class-level semantic extraction does not keep
Compilation/ISymbol objects in the output model; equatable source strings prevent
unchanged output from propagating. No performance or build-only scheduling guarantee
is claimed.

Registration declarations are emitted through RegisterSourceOutput. Internal
implementation uses RegisterImplementationSourceOutput, so a host can omit it
while retaining the API needed by editor analysis. No module initializer, runtime
reflection-based scan or static global catalog is installed.

See [the runtime guide](../FlowOnRails.AspNetCore/README.md) for configuration,
status/projection rules, diagnostics and a runnable sample. The mapping analyzer
uses the public analysis API and does not require enabling the base diagnostics
for local handlers. Referenced producers must export their summaries.

`FORHTTP009` warns when a known exact error requires a specific translation but
no authorized map is registered. `FORHTTP010` reports contexts where configuration
or endpoint error flow cannot be determined. Generic visibility does not require a map,
and unknown controllers do not disable independent controller scopes. The analyzer
never executes application callbacks or removes the runtime fallback.
