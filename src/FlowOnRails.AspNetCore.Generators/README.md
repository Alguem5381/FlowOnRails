# FlowOnRails.AspNetCore.Generators

Incremental Roslyn generator for the exact HTTP mappings in FlowOnRails.AspNetCore.
Targets netstandard2.0 and uses Roslyn 5.0 to support the solution's .NET 10/C# 14
consumer syntax. Reference it as an analyzer, not as a runtime assembly.

The generator finds attributed configuration classes, validates methods and static
metadata, discovers accessible closed error contracts in selected assemblies and
emits typed registrations/projectors. Class-level semantic extraction does not keep
Compilation/ISymbol objects in the output model; equatable source strings prevent
unchanged output from propagating. Selected-assembly discovery still has a cost;
no performance or build-only scheduling guarantee is claimed.

Registration declarations are emitted through RegisterSourceOutput. Internal
implementation uses RegisterImplementationSourceOutput, so a host can omit it
while retaining the API needed by editor analysis. No module initializer, runtime
reflection-based scan or static global catalog is installed.

See [the runtime guide](../FlowOnRails.AspNetCore/README.md) for configuration,
status/projection rules, diagnostics and a runnable sample.
