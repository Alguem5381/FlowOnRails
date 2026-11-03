# FlowOnRails operation semantics

This is the contract for the current 0.2.0 development line. Domain result variants belong to the application's value types. Semantic success records are deferred; see the [design note](design/semantic-success.md).

## States and construction

A valid rail is either successful or failed:

| State | Control flag | Stored error | Value |
|---|---|---|---|
| Successful `Rail` | `true` | `null` | None |
| Successful `RailFor<T>` | `true` | `null` | Non-null `T` |
| Failed rail | `false` | Non-null `Error` | Unused |
| Uninitialized struct | `false` | `null` | Default; invalid |

`Rail.Ok()`, `Rail.Ok(value)`, `Rail.Fail(error)` and `Rail.Fail<T>(error)` are the public factory facade. Task factories construct the same states inside completed tasks. Implicit conversions from values and errors obey the same rules. Factories reject null values and null errors.

Observing or operating on `default(Rail)` or `default(RailFor<T>)` throws `InvalidOperationException`. This is a programming error and cannot be recovered as a domain failure. `Bind`, `Check` and `RecoverWith` also reject uninitialized callback results before returning them.

## Observation

`IsSuccess` validates initialization; `IsFailure` is its negation.

`TryUnwrap` returns `true` on success, exposing the value for typed rails and a null error. On failure it returns `false`, exposing the original non-null error and a default value where applicable. Nullability annotations associate successful extraction with non-null reference values and failed extraction with a non-null error.

`Match` invokes exactly one callback. The successful callback receives the value for typed rails; the failed callback receives the original error.

There is no `Outcome` hierarchy or semantic success object in this development line. No `Deconstruct` is provided: a void deconstruction cannot express the same conditional nullability guarantee as `TryUnwrap`.

## Operations

| Operation | Successful source | Failed source |
|---|---|---|
| `Map` | Invoke once and wrap the returned non-null value | Skip callback; preserve error |
| `Bind` | Invoke once and return its valid rail, including a new failure | Skip callback; preserve error |
| `Check` | Invoke once; preserve source on success, discard callback value, or return its original error on failure | Skip callback; preserve source |
| `Ensure` | Invoke predicate once; preserve source when true, fail with supplied error when false | Skip predicate; preserve source |
| `Tap` | Invoke action once; preserve source | Skip action; preserve source |
| `TapIf(bool, action)` | Invoke action once only when condition is true | Skip action; preserve source |
| `TapIf(predicate, action)` | Invoke predicate once; invoke action only when true; preserve source | Skip both; preserve source |
| `MapError` | Skip callback; preserve source | Invoke once and replace error |
| `RailFor<T>.Recover` | Skip callback; preserve source | Invoke once and wrap returned non-null value |
| `RecoverWith` | Skip callback; preserve source | Invoke once and return its valid fallback rail, which may fail |

`Bind` supports both transitions between rails with and without values. Typed-to-untyped conversion removes the value and preserves the control state and error.

`Check` accepts a callback returning either `Rail` or `RailFor<TChecked>`. Typed sources pass their value to the callback and retain that same value on success. Untyped sources pass no value and continue without one. `CheckAsync` provides the same semantics for Task and ValueTask sources. It accepts synchronous rail callbacks or asynchronous callbacks from the source family. The source and any asynchronous callback are awaited once, and the returned awaitable preserves the source family. No implicit Task-to-ValueTask callback bridge is added.

Without semantic successes, `BindKeepValue` would duplicate `Check`, and `BindKeepSuccess` would duplicate `Bind`. No separate operations with those names are provided. `MapSuccess` does not apply to this model.

The untyped rail has no value-producing `Recover`; use `RecoverWith` with a fallback returning `Rail`. There is no automatic exception handling: callback exceptions propagate. Domain errors model explicit failures.

## Asynchronous boundaries

- Task extensions return `Task`; ValueTask extensions return `ValueTask`.
- Sources are awaited before inspecting their rail. Applicable asynchronous callbacks are awaited once before returning the result.
- ValueTask sources and callback results are consumed once, including sources backed by `IValueTaskSource<T>`.
- Callbacks use the same asynchronous family as their source. The existing typed Task-to-ValueTask binding bridge is named `BindValueTaskAsync` and returns `Task`, avoiding ambiguity for `BindAsync(async ...)` lambdas.
- `RecoverAsync` produces a replacement value for a typed rail. `RecoverWithAsync` returns a fallback rail for either shape.
- Task exceptions and cancellation propagate; they are not converted into `Error`.
- Converting families requires an explicit boundary. A mismatched awaitable can otherwise be wrapped as a mapped value or discarded by an `Action`; keep compiler warnings enabled.

## LINQ and sequences

Query `Select` derives from `Map`; query `SelectMany` derives from `Bind`. A failed step skips later callbacks and projections.

Sequence operations have a different scope: each element is an independent rail.

| Operation | Contract |
|---|---|
| Sequence `Map` / `Bind` | Deferred; transform each successful element, preserve each failure and continue enumerating later elements |
| `SelectOks` / `SelectErrors` | Deferred; project successful values or original error objects |
| `Partition` | Enumerate once and return ordered lists of values and errors |
| `Combine` | Enumerate once; return the ordered values if all succeed, or stop at the first failure and return its original error |

An empty `Combine` succeeds with an empty list. Early failure disposes the enumerator. Re-enumerating a deferred sequence repeats its callbacks. Uninitialized elements throw when encountered. Error aggregates defined by the consumer remain opaque and are not flattened.

## Equality and laws

Equality compares control state and domain errors using their record equality. Successful typed rails also compare values using `EqualityComparer<T>.Default`. Failure equality ignores the unused value. Hashes use the same components. Different concrete error record types remain unequal even with matching fields.

For valid rails and pure callbacks:

```text
Ok(a).Bind(f)             == f(a)
m.Bind(Ok)               == m
m.Bind(f).Bind(g)         == m.Bind(x => f(x).Bind(g))
m.Map(identity)          == m
m.Map(f).Map(g)           == m.Map(x => g(f(x)))
```

Failures absorb happy-path operations without invoking their callbacks. These laws do not imply transactionality, rollback or execution only once across multiple enumerations.

## Internal nullable analysis

`IsSuccess` and `IsFailure` use `MemberNotNullWhen` to tell the compiler that the
internal `Error` member is non-null on failure. This works even though consumers
cannot access that member. State validation still runs before returning the
boolean; attributes describe the contract rather than replacing runtime checks.

`TValue : notnull` and the successful-value constructor check retain the value
contract. A failed rail's value storage is unused. Public callers continue to use
`TryUnwrap`, whose conditional output annotations describe both success and
failure without exposing internal members.
