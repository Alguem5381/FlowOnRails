# FlowOnRails

This is my first public library. I originally created it to solve my own difficulties with error handling in C# and decided to share it. I am new to creating open source packages and still learning without strict guidance. Any feedback or contribution is welcome.

FlowOnRails is a library built to bring Railway Oriented Programming to C# natively and with a focus on developer experience.

## Features

- Allocation Conscious. Built with readonly partial structs. Neutral successful results add no heap allocation; rich outcomes allocate only when the domain chooses to create one.
- Strong Typing. It avoids using strings or hidden exceptions for errors. You create your own error records inheriting from a base Error class.
- LINQ Support. You can write complex flows using native C# query syntax.
- Async Integration. Built-in extensions for Task and ValueTask.
- Flow Transitions. Fluid methods like Bind, Map, and MapError allow you to seamlessly transition between void operations and operations that return values.
- Semantic Successes. Successful operations can optionally carry strongly typed domain meaning without allocating for the default case.

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

### Defining Semantic Successes

Successful operations may also carry a domain-specific success record. The value remains the product of the computation; the success record describes what the operation means.

```csharp
public record UserCreated(Guid Id) : Success;

var result = Rail.Ok(user, new UserCreated(user.Id));
var completed = Rail.Succeed(new UserCreated(user.Id)); // Successful operation without a value
```

When successful operations are bound, the last explicit success wins. A normal `Rail.Ok(value)` has a neutral success and therefore does not erase an earlier rich success. See the [operation semantics](https://github.com/Alguem5381/FlowOnRails/blob/main/docs/operation-semantics.md) for the complete state-transition table and laws.

The library treats domain success records as opaque objects. If your domain defines an aggregate success, the same replacement rule applies to the whole object; FlowOnRails does not automatically merge its contents.

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

## Contributing

Feel free to open an Issue or submit a Pull Request if you find a bug or want to propose improvements.

## License

Distributed under the MIT License. See the LICENSE file for more details.
