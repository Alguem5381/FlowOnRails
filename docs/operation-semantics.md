# FlowOnRails operation semantics

This document specifies the synchronous core of FlowOnRails 0.2.0. It is the behavioral contract for the public API. LINQ and asynchronous extensions must preserve these rules rather than define independent semantics.

## 1. Mental model

A valid rail is in exactly one control state:

```text
Failure(error)
Success(value?, semanticSuccess)
```

The model has three separate dimensions:

| Dimension | Question answered | Rule |
|---|---|---|
| Control | May the pipeline continue? | Success continues; failure short-circuits. |
| Value | What did the computation produce? | Only `RailFor<TValue>` carries a value. |
| Semantic outcome | What does the result mean to the domain? | `Success` describes the happy path; `Error` describes the failure path. |

Only the control state determines short-circuiting. A rich semantic `Success` never stops or redirects the pipeline.

`Outcome` is the closed root of the semantic outcome hierarchy:

```text
Outcome
├── Success   <- applications may derive domain successes
└── Error     <- applications may derive domain errors
```

Applications cannot derive directly from `Outcome`. They extend one of its two public branches.

## 2. Valid states and internal representation

The notation used throughout this document is:

- `F(e)`: failure containing error `e`.
- `S(s)`: successful `Rail` with semantic success `s` and no value.
- `S(v, s)`: successful `RailFor<T>` containing value `v` and semantic success `s`.
- `I`: the neutral semantic success.

Each rail stores one control flag and one `Outcome?` reference. A typed rail additionally stores its value.

| Logical state | Control flag | Stored outcome | Stored value |
|---|---:|---|---|
| `S(I)` | `true` | `null` | — |
| `S(s)` | `true` | rich `Success` | — |
| `S(v, I)` | `true` | `null` | `v` |
| `S(v, s)` | `true` | rich `Success` | `v` |
| `F(e)` | `false` | `Error` | unused |
| Uninitialized | `false` | `null` | default |

The first five rows are valid algebraic states. The last row is the CLR zero-initialized struct state and is deliberately invalid.

Observing or operating on `default(Rail)` or `default(RailFor<T>)` throws `InvalidOperationException`. This is a programming defect, not a domain failure, so it cannot be recovered through `Recover` or `RecoverWith`.

### Neutral semantic success

`I` lets ordinary successes participate in the same algebra as rich domain successes without allocating an object for every successful rail.

Internally, `I` is stored as `null`. When an API explicitly requests the semantic outcome, FlowOnRails materializes a shared immutable `Success` singleton. Repeated observation of neutral successes therefore returns the same instance.

Passing an observed neutral success back to `Rail.Succeed` or `Rail.Ok(value, success)` restores the same internal neutral representation. This also applies to record copies made with `with`. Equality, hash codes, and composition are therefore unchanged by observing and reconstructing a neutral rail. Ordinary neutral construction does not initialize the singleton.

Domain records remain opaque. An empty domain-defined aggregate is still a rich success; only the library's neutral representation is normalized. No operation inspects or automatically merges a domain aggregate's contents.

Semantic successes compose with `⊕`:

```text
S  ⊕ I  = S
I  ⊕ S  = S
S1 ⊕ S2 = S2   when S1 and S2 are both rich
```

In words: neutral success is the identity, and the last rich success wins. This operation is associative.

## 3. Construction

`Rail` is the single public factory facade. `RailFor<T>` intentionally exposes no separate public `Ok` or `Fail` factories.

| Factory | Result |
|---|---|
| `Rail.Ok()` | `S(I)` |
| `Rail.Succeed(s)` | `S(s)` |
| `Rail.Ok(v)` | `S(v, I)` |
| `Rail.Ok(v, s)` | `S(v, s)` |
| `Rail.Fail(e)` | `F(e)` as an untyped rail |
| `Rail.Fail<T>(e)` | `F(e)` as a typed rail |

`Succeed` is separate from `Ok(value)` so a domain `Success` subtype cannot be captured by generic value inference.

Factories reject null values, null errors, and null rich successes. Async factories create the same states inside a completed `Task`; they do not define different semantics.

The existing implicit conversions are construction shorthand and produce the same states:

| Conversion | Equivalent construction |
|---|---|
| `TValue -> RailFor<TValue>` | `Rail.Ok(value)` |
| `Error -> Rail` | `Rail.Fail(error)` |
| `Error -> RailFor<TValue>` | `Rail.Fail<TValue>(error)` |

## 4. Observation

Observation does not change a rail.

### `IsSuccess` and `IsFailure`

Both properties validate initialization. `IsFailure` is defined as the negation of `IsSuccess`, so initialization logic has one entry point.

### `Outcome`

`Outcome` exposes the active semantic branch:

| Rail state | Observed outcome |
|---|---|
| `F(e)` | the original `Error e` |
| `S(s)` or `S(v, s)` | the original rich `Success s` |
| `S(I)` or `S(v, I)` | the shared neutral `Success` singleton |

Reading `Outcome` is one of the operations that may materialize the neutral singleton.

### `TryUnwrap`

The basic overloads expose only the value and/or error. They do not materialize the neutral success.

The rich overloads also expose `Success`; on a neutral successful rail they materialize the shared singleton.

The returned boolean is the control discriminant:

| Return value | Guaranteed output |
|---:|---|
| `true` | successful value where applicable, plus non-null `Success` in the rich overload |
| `false` | non-null `Error` |

Nullability annotations connect these guarantees to the returned boolean.

### `Match`

`Match` invokes exactly one callback. Existing overloads expose the value and/or error without observing semantic success. Rich overloads additionally pass a non-null `Success` to the successful callback and therefore materialize `I` when necessary.

### Why there is no `Deconstruct`

`RailFor<T>` deliberately has no `Deconstruct(out Outcome, out T?)`. A `void` deconstruction method cannot express the nullability relationship “the value is non-null when the outcome is `Success`.” Adding it would provide weaker compiler guarantees than `TryUnwrap` and would make the two extraction APIs disagree.

Deconstruction remains excluded until C# or a future representation can preserve that relationship without lying about nullability.

## 5. Core operations

### Value transformation and railway composition

| Operation | Input | Callback | Result |
|---|---|---|---|
| `Map(f)` | `F(e)` | Not invoked | `F(e)` |
| `Map(f)` | `S(v, s)` | Produces value `u` | `S(u, s)` |
| `Map(f)` on untyped `Rail` | `S(s)` | Produces value `u` | `S(u, s)` |
| `Bind(f)` | `F(e)` | Not invoked | `F(e)` |
| `Bind(f)` | `S(v, s1)` or `S(s1)` | Produces `F(e2)` | `F(e2)` |
| `Bind(f)` | `S(v, s1)` or `S(s1)` | Produces success with `s2` | New value shape with `s1 ⊕ s2` |

`Map` changes only the computational value. `Bind` adopts the returned rail's value shape and composes semantic successes.

When a callback returns a failure, previous values and successes are discarded. No operation carries an earlier `Success` into a new failure.

Callbacks are not exception boundaries. If a callback throws, the exception propagates to the caller; FlowOnRails does not convert it into an `Error`. Errors model expected domain failures, while exceptions still represent exceptional or programming failures.

### Validation and side effects

| Operation | Input | Callback execution | Result |
|---|---|---|---|
| `Ensure(predicate, e2)` | `F(e)` | Predicate not invoked | `F(e)` |
| `Ensure(predicate, e2)` | Success, predicate `true` | Invoked once | Original success unchanged |
| `Ensure(predicate, e2)` | Success, predicate `false` | Invoked once | `F(e2)`; value and success are discarded |
| `Tap(action)` | `F(e)` | Not invoked | Original `F(e)` |
| `Tap(action)` | Success | Invoked once | Original success unchanged |
| `TapIf(bool, action)` | `F(e)` | Action not invoked | Original `F(e)` |
| `TapIf(bool, action)` | Success, condition `false` | Action not invoked | Original success unchanged |
| `TapIf(bool, action)` | Success, condition `true` | Action invoked once | Original success unchanged |
| `TapIf(predicate, action)` | `F(e)` | Neither callback invoked | Original `F(e)` |
| `TapIf(predicate, action)` | Success, predicate `false` | Predicate invoked once; action not invoked | Original success unchanged |
| `TapIf(predicate, action)` | Success, predicate `true` | Both invoked once | Original success unchanged |

`Tap` operations are limited to callbacks returning `void`. A callback returning another rail is railway composition and belongs to `Bind`, not `Tap`.

### Error transformation and recovery

| Operation | Input | Callback result | Final result |
|---|---|---|---|
| `MapError(f)` | Success | Callback not invoked | Original success |
| `MapError(f)` | `F(e)` | Error `e2` | `F(e2)` |
| `RailFor<T>.Recover(f)` | Success | Callback not invoked | Original success |
| `RailFor<T>.Recover(f)` | `F(e)` | Value `v` | `S(v, I)` |
| `Rail.Recover(f)` | Success | Callback not invoked | Original success |
| `Rail.Recover(f)` | `F(e)` | Rich success `s` | `S(s)` |
| `RecoverWith(f)` | Success | Callback not invoked | Original success |
| `RecoverWith(f)` | `F(e)` | Any rail `r` | Exactly `r` |

`RecoverWith` replaces the failed rail completely. The old error is neither retained nor combined with the returned rail.

`OrElse` is reserved for a future alternative computation that does not receive the previous error.

### Typed-to-untyped conversion

Conversion from `RailFor<T>` to `Rail` removes only the computational value:

```text
F(e)     -> F(e)
S(v, s)  -> S(s)
```

The semantic success, including neutrality, is preserved without forcing materialization.

## 6. Derived APIs

- Task and ValueTask extensions await the asynchronous boundary and then delegate composition to the synchronous core.
- Task extensions return Task; ValueTask extensions return ValueTask. ValueTask sources and callbacks are consumed once.
- A Task-backed typed rail can bind a ValueTask callback explicitly through `BindValueTaskAsync`; its result remains Task-backed. The distinct name avoids ambiguity for ordinary `BindAsync(async ...)` lambdas.
- Task exceptions and cancellation propagate through the asynchronous operation. They are not converted into domain errors.
- LINQ `Select` derives from `Map`.
- LINQ `SelectMany` derives from `Bind` and therefore uses the same `⊕` composition and failure short-circuit.
- `SelectOks`, `SelectErrors`, and `Partition` are terminal projections. Their returned collections no longer carry railway semantics.
- Sequence `Combine` semantics for rich successes are intentionally unspecified in 0.2.0. Existing collection behavior remains unchanged until ordering and aggregation are designed independently.

## 7. Equality

Two rails are equal when they have the same control state and equal semantic outcomes.

For successful `RailFor<T>` values, the computational values must also be equal. Failure equality ignores the unused stored value.

Outcome equality uses record value equality. Records of different concrete types are not equal, even when their properties contain equal data. Hash codes use the same state components as equality.

## 8. Laws

For valid rails and pure callbacks:

```text
Ok(a).Bind(f)              == f(a)                         left identity
m.Bind(Ok)                 == m                            right identity
m.Bind(f).Bind(g)          == m.Bind(x => f(x).Bind(g))   associativity
m.Map(identity)            == m                            map identity
m.Map(f).Map(g)            == m.Map(x => g(f(x)))         map composition
```

Failure absorption applies to every happy-path callback:

```text
F(e).Map(f)      == F(e)
F(e).Bind(f)     == F(e)
F(e).Ensure(...) == F(e)
F(e).Tap(...)    == F(e)
```

The callbacks in those expressions are not invoked.

## 9. Deferred operations

These operations identify useful points in the state-transformation matrix but are not part of 0.2.0:

| Candidate | Successful value | Semantic success | Failure behavior |
|---|---|---|---|
| `Check` | Preserve previous value | Preserve previous success | Short-circuit with new error |
| `MapSuccess` | Preserve value | Transform current success | Preserve existing failure |
| `BindKeepSuccess` | Use returned value | Preserve previous success | Short-circuit with returned error |
| `BindKeepValue` | Preserve previous value | Compose with returned success | Short-circuit with returned error |

A possible generic discriminated representation such as `Success<T>` is also deferred. It must be evaluated separately because it affects nullability, allocation, naming, and the distinction between computational value and semantic success.
