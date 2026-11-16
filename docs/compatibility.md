# .NET 10 and .NET 11 compatibility

FlowOnRails 0.3.0 keeps its `net10.0` runtime asset. Applications targeting
`net10.0` or `net11.0` can reference the same package. A separate `net11.0`
asset is unnecessary until the library uses APIs specific to that runtime.
The current ASP.NET Core integration follows the same policy.

The Analysis API, analyzers, code fixes and ASP.NET generator remain
`netstandard2.0`. Their minimum Roslyn dependency remains 5.0.0, preserving the
.NET 10 compiler host. The compatibility fixtures also load the packaged API
inside Roslyn 5.12.0, shipped with SDK 11.0.100. Changing the runtime alone does
not update the Roslyn packages of a satellite tool: use a compiler host capable
of parsing the language version of the input.

## Build selection

- The root `global.json` selects SDK 10.0.100 with `latestPatch`: servicing updates
  within the 10.0.1xx feature band are accepted; SDK 11 is not selected silently.
- `tests/compatibility/global.json` selects SDK 11.0.100 with the same patch policy.
- Both policies reject prerelease SDKs. Compiler-tool source explicitly uses
  C# 14.0 rather than the machine-dependent `latest` setting.
- CI and CD install both SDK feature bands. Packages are built and packed with
  SDK 10; the test projects can select .NET 11 independently of runtime packages.

To run the existing suite on the new runtime, from the repository root:

```sh
cd tests/compatibility
dotnet test ../../FlowOnRails.slnx -c Release -p:FlowOnRailsTestTargetFramework=net11.0
```

Running the ordinary solution commands from the repository root continues to use
SDK 10 and test .NET 10. Selecting SDK 11 for the tests does not retarget the
library or raise the minimum compiler dependency of its tooling packages.

## C# 15 interactions

The isolated package fixtures compile real consumer projects with the SDK 11
compiler. They run the results, inspect analyzer diagnostics and reference-assembly
summaries, and query the public API with that same compiler's symbols and CFGs.

| Feature | Covered interaction |
| --- | --- |
| Union types | A union is a successful `RailFor<TValue>` payload; matching a union case can explicitly produce a failed rail |
| Closed hierarchies | Domain errors derive from a closed family that itself derives from the package's open `Error` base |
| Collection expression arguments | Constructor arguments and collection contents coexist with subsequent error flow; a constructor argument can update a tracked rail through `ref` |
| Labeled break/continue | Assignments before jumps reach the correct continuation, locally and in exported summaries |
| Extension indexers | A rail-returning extension getter is followed and its error survives exported metadata |
| Async | Task/ValueTask methods, asynchronous Bind/MapError callbacks and exported summaries |

An `Error` inside a successful union payload does not become a failed rail. As
with other payload types, failure requires an explicit error/rail operation.
The base `Error` remains open so applications can define errors in other
assemblies. A `closed` error family does not close an interface's ResultsWith
contract: an implementation can still return errors outside that family.

The fixtures also run with `<Features>runtime-async=on</Features>` on the .NET 11
consumer projects. This validates their interaction with the existing library;
the shipped `net10.0` library itself does not opt into runtime-native async.
These checks do not claim complete interpretation of C# 15, measure performance,
or enable the preview memory-safety rules. Existing analysis limits still apply.
Editor presentation remains dependent on the IDE and its Roslyn host.

## Package verification

After packing the three projects into a clean directory:

```sh
python3 scripts/verify-packages.py ./artifacts/packages --framework net10.0 --framework net11.0
python3 scripts/verify-packages.py ./artifacts/packages --framework net11.0 --runtime-async
```

Each framework uses its own SDK policy and fresh package cache outside the
repository. Fixtures use NuGet references rather than references to library
projects. Compiler/analyzer/generator warnings fail verification. The .NET 11
API fixture deliberately substitutes the SDK's Roslyn assemblies for the minimum
NuGet dependency so it exercises new semantic operations, not just a new runtime
hosting the old parser. The .NET 10 API fixture retains the normal NuGet dependency.

Both validations run before authentication/publication in CD. Integration releases
also verify their published base dependency on both frameworks. See
[publishing](publishing.md) for package order and feed configuration.

References: [SDK selection](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json),
[C# 15 features](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-15),
[.NET 11 runtime](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/runtime).
