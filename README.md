# FlowOnRails

This is my first public library. I originally created it to solve my own difficulties with error handling in C# and decided to share it. I am new to creating open source packages and still learning without strict guidance. Any feedback or contribution is welcome.

FlowOnRails is a library built to bring Railway Oriented Programming to C# natively and with a focus on developer experience.

## Features

- Value Types. Results use readonly partial structs; the application defines its values and errors.
- Strong Typing. It avoids using strings or hidden exceptions for errors. You create your own error records inheriting from a base Error class.
- LINQ Support. You can write complex flows using native C# query syntax.
- Async Integration. Built-in extensions for Task and ValueTask.
- Flow Transitions. Fluid methods like Bind, Map, and MapError allow you to seamlessly transition between void operations and operations that return values.

## Installation

```bash
dotnet add package FlowOnRails
```

## Usage

### Defining Errors

You can create errors using records:

```csharp
public record ValidationError(string Message) : Error;
public record NotFoundError(string Message) : Error;
```

### Returning a Rail

The library uses Rail for flows that do not return data and RailFor for flows with data.

```csharp
public RailFor<User> GetUser(int id)
{
    if (id <= 0)
        return Rail.Fail<User>(new ValidationError("ID must be positive."));

    var user = Database.Find(id);
    if (user is null)
        return Rail.Fail<User>(new NotFoundError("User not found."));

    return Rail.Ok(user);
}
```

### Checking a fallible operation while keeping the value

`Check` runs another operation only when the current rail succeeds. If the checked operation succeeds, the original value continues through the pipeline; any value produced by that operation is discarded. If it fails, its error stops the pipeline.

```csharp
public RailFor<User> PrepareUser(int id)
{
    return GetUser(id)
        .Check(user => ValidatePermissions(user)) // Returns Rail
        .Check(user => LoadPreferences(user));    // Returns RailFor<Preferences>; value is discarded
}
```

The callbacks can return `Rail` or `RailFor<T>`. `Check` is also available on an untyped `Rail`, which continues without a value. Use `Bind` when the next operation's value should replace the current value, `Ensure` for a predicate with a supplied error, and `Tap` for an action without a rail result.

`Check` currently belongs to the synchronous core. Callback exceptions propagate, and uninitialized callback results are rejected.

### Using LINQ

You can use LINQ to chain logic. If any step fails, the flow is interrupted and the error is returned immediately:

```csharp
public RailFor<Receipt> ProcessCheckout(int userId, Cart cart)
{
    var result = 
        from user in GetUser(userId)
        from stock in CheckStock(cart)
        from payment in ProcessPayment(user, cart)
        select GenerateReceipt(payment);

    return result;
}
```

### Unwrapping

When you need to extract the final result, you can use the TryUnwrap method:

```csharp
var rail = GetUser(10);

if (!rail.TryUnwrap(out var user, out var error))
{
    Console.WriteLine($"Failed: {error}");
    return;
}

Console.WriteLine($"Success: {user.Name}");
```

### Async Extensions

FlowOnRails provides extensions like BindAsync, MapAsync and MapErrorAsync for asynchronous operations:

```csharp
var result = await GetUserAsync(id)
    .MapErrorAsync(e => new DomainError($"Failed to fetch user: {e}"))
    .BindAsync(user => ProcessUserAsync(user));
```

### Async families and migration to 0.2.0

Task extensions return `Task`; ValueTask extensions return `ValueTask`, so a pipeline stays in the same asynchronous family. Use asynchronous callbacks from that family. For example:

```csharp
ValueTask<RailFor<int>> result = ValueTask.FromResult(Rail.Ok(1))
    .MapAsync(value => value + 1)
    .MapAsync(value => ValueTask.FromResult(value + 1));
```

For `Task<RailFor<T>>` sources, callbacks returning `ValueTask` use the explicit `BindValueTaskAsync` bridge, which returns a `Task`:

```csharp
var result = await Rail.OkAsync(1)
    .BindValueTaskAsync(value => ValueTask.FromResult(Rail.Ok(value + 1)));
```

Migration from the previous signatures:

- ValueTask extension results now have type `ValueTask<...>`. Existing `await` calls still work; use `.AsTask()` when another API requires a `Task`.
- The two `BindAsync` overloads on `Task<RailFor<T>>` that accepted ValueTask callbacks are now named `BindValueTaskAsync`. Ordinary Task callbacks and `async` lambdas continue to use `BindAsync` without ambiguous overloads.
- When mixing other callback types, convert explicitly at the boundary. A synchronous `MapAsync` overload can wrap an awaitable as a value, and an expression passed as `Action` can discard a returned awaitable. Keep compiler warnings enabled.

### Recovery

Use `Recover` on `RailFor<T>` to produce a replacement value. Use `RecoverWith` to run a fallback that returns another rail, which may itself fail:

```csharp
var value = Rail.Fail<int>(new NotFoundError("Missing value"))
    .Recover(_ => 42);

var fallback = Rail.Fail<int>(new NotFoundError("Missing value"))
    .RecoverWith(_ => Rail.Ok(42));

var completed = Rail.Fail(new NotFoundError("Missing operation"))
    .RecoverWith(_ => Rail.Ok());
```

Task and ValueTask extensions provide `RecoverAsync` for typed replacement values and `RecoverWithAsync` for fallback rails. Callbacks should return the same asynchronous family as the source.

### Additional changes in 0.2.0

- Construct results through `Rail.Ok(...)` and `Rail.Fail<T>(error)`. The old public `RailFor<T>.Ok` and `RailFor<T>.Fail` factories are no longer public.
- `IRail` and its explicit `TryGetError` method have been removed. Use `IsFailure`, `TryUnwrap` or `Match` on the concrete rail type.
- Calls to the previous `Recover` / `RecoverAsync` that return a rail should use `RecoverWith` / `RecoverWithAsync`.
- Observing or operating on `default(Rail)` or `default(RailFor<T>)` throws `InvalidOperationException`. Construct a successful or failed result explicitly. `Bind`, `Check` and `RecoverWith` also reject an uninitialized rail returned by a callback.
- Factories reject null values and null errors. Callback exceptions and cancellation propagate rather than becoming domain errors.

See [operation semantics](https://github.com/Alguem5381/FlowOnRails/blob/main/docs/operation-semantics.md) for the current contract. Semantic success records are deferred; the rationale and archived implementation are described in the [design note](https://github.com/Alguem5381/FlowOnRails/blob/main/docs/design/semantic-success.md). Domain result variants belong in the application's value types.

## Contributing

Feel free to open an Issue or submit a Pull Request if you find a bug or want to propose improvements.

## License

Distributed under the MIT License. See the LICENSE file for more details.
