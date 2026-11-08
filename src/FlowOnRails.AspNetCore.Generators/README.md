# FlowOnRails.AspNetCore.Generators

Incremental Roslyn generator for the exact HTTP mappings in FlowOnRails.AspNetCore.
Targets netstandard2.0 and uses Roslyn 5.0 to support the solution's .NET 10/C# 14
consumer syntax. It is bundled under `analyzers/dotnet/cs` in the
`FlowOnRails.AspNetCore` NuGet package; no separate generator package is needed.
When developing from source, reference it as an analyzer, not as a runtime assembly.

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
status/projection rules, diagnostics and a runnable sample.
