# Resource lifetimes and scoped operations

Status: deferred pending a concrete consumer use case (2026-11-03).

FlowOnRails propagates values and domain errors. Holding an `IDisposable` or
`IAsyncDisposable` value does not transfer ownership of that resource to the rail.
The application decides when the resource should be disposed.

## Use the language scopes first

C# already guarantees disposal when a `using` scope exits, including on return or
exception. `await using` awaits asynchronous disposal. A callback can establish
that scope without a new rail operation:

```csharp
var result = await GetRequestAsync(id)
    .BindAsync(async request =>
    {
        await using var session = await OpenSessionAsync(request);
        return await ReadSnapshotAsync(session);
    });
```

In this example, `ReadSnapshotAsync` must finish using the session before it
returns. Its result must be independent of the disposed session. Returning the
session itself, a deferred sequence, or another object that depends on it would
outlive the scope. A failed rail returned by the callback still exits the scope
and disposes the session. Exceptions, cancellation and disposal exceptions keep
the normal C# behavior; the library does not convert them into domain errors.

A caller can also unwrap an acquired resource and use `using` or `await using`
around the remainder of its work. This is useful when its lifetime spans several
pipelines.

## When a library operation might help

Revisit a scoped `Using` / `UsingAsync` combinator if consumers repeatedly need to
acquire a resource, run an entire rail pipeline inside its scope, and dispose it
on every exit path. Before adding it, settle:

- Whether acquisition is a factory and can itself return a failed rail.
- Whether ownership is explicitly transferred to the operation.
- How synchronous/asynchronous acquisition, work and disposal compose.
- Which cancellation and exception rules apply, including disposal failures.
- How documentation makes escaping a disposed resource or deferred work clear.

Do not make `Rail` or `RailFor<T>` implement disposal on behalf of their values.
These are copyable structs, so automatic forwarding would make ownership unclear
and could dispose a shared resource multiple times. A scoped combinator would
need an explicit lifetime contract rather than forwarding disposal from a rail.

No public disposal operation is added for now. Stabilizing the existing
composition and nullability contracts has a demonstrated use case.

References:

- [C# using statement](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/using)
- [Nullable analysis attributes](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis)
