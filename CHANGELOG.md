# Changelog

## [Unreleased]

### Added

- `WithErrorVisibility` for Minimal API endpoints and nested route groups, with endpoint-to-global precedence and independent immutable configuration snapshots.

- Initial `FlowOnRails.AspNetCore` integration with tree-based error visibility, ancestor projection candidates, exact-controller overrides, validated immutable configuration and ASP.NET endpoint metadata integration.
- Exact HTTP mappings, generated automatic property projectors, manual extensions, request-aware generic problems and native HTTP/MVC result adapters.
- `FlowOnRails.AspNetCore` 0.1.0 packaging with the source generator included, public usage documentation and independent `aspnetcore/v<version>` release tags. Package consumption is validated through isolated consumers; satellite releases also verify the published base dependency before upload.

- `TapError` and Task/ValueTask `TapErrorAsync` to observe the original error on failure while preserving the rail. Asynchronous observers are awaited.
- Lazy `Ensure` / `EnsureAsync` error-factory overloads, allowing errors to include the current value and creating them only when the predicate fails.

- `CheckAsync` on Task and ValueTask sources with or without a value. Checks accept synchronous rails or awaitable rails from the source family and preserve the original value on success.

### Changed

- Organized the unpublished ASP.NET visibility implementation into configuration, rules, resolution and integration. Role-based names such as `PolicyBuilder`, `RuleScope`, `ErrorPolicy` and `RuleIndex` replace repeated `Visibility*` prefixes. Shared configuration uses `builder.Global`; selection and conflict checks use explicit cases rather than enum ordering.
- Visibility resolution uses frozen indexes by scope and error root, avoiding full rule scans and sorting during requests.

- Internal error access is described by `MemberNotNullWhen` on state properties, removing null-forgiving operators after state checks while retaining runtime validation.

## [0.3.0] - 2026-11-13

- Includes opt-in error-flow analysis in the base package. Set
  `FlowOnRailsEnableFlowAnalysis=true` to enable diagnostics and summary export.
- Adds `ResultsWith` contracts and cross-assembly error-flow summaries.
- Validates package consumers on .NET 10 and .NET 11, including C# 15 scenarios.

## [0.2.0] - 2026-11-02

### Added

- Synchronous `Check` operations on `Rail` and `RailFor<T>`. Checked operations may return a rail with or without a value. A successful check preserves the source value; a failed check propagates its original error.
- Public API tests for state transitions, callback execution, LINQ, composition laws, asynchronous waiting, exceptions, cancellation and single-use ValueTask sources.
- Release-based NuGet publishing with tests and release-tag validation before authentication and upload.

### Fixed

- ValueTask extension chains preserve their asynchronous family and await applicable callbacks.
- Task-backed `BindAsync` accepts ordinary async lambdas without ambiguous overload resolution.
- Factories reject null values and errors. Uninitialized rails are rejected when observed or used, including callback results from `Bind`, `Check` and `RecoverWith`.
- README examples and documentation now match the public API. The `GetUser` example is compiled and checked against the documented snippet.

### Breaking changes and migration

- Construct typed rails through `Rail.Ok(value)` and `Rail.Fail<T>(error)`. `RailFor<T>.Ok` and `RailFor<T>.Fail` are no longer public.
- `IRail` and its explicit `TryGetError` member were removed. Use the concrete rail's `IsFailure`, `TryUnwrap` or `Match`.
- Fallbacks returning a rail now use `RecoverWith` / `RecoverWithAsync`. `Recover` / `RecoverAsync` produce a replacement value on typed rails.
- ValueTask extensions return `ValueTask` rather than `Task`. Await them normally, or use `AsTask()` where required.
- The Task-backed binding overloads accepting ValueTask callbacks are named `BindValueTaskAsync`.
- `default(Rail)` and `default(RailFor<T>)` are invalid; explicitly construct a successful or failed rail.

The package targets .NET 10. Semantic success records remain deferred and are not included in this release.

[0.2.0]: https://github.com/Alguem5381/FlowOnRails/releases/tag/v0.2.0
