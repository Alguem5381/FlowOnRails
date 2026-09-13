# FlowOnRails

This is my first public library. I originally created it to solve my own difficulties with error handling in C# and decided to share it. I am new to creating open source packages and still learning without strict guidance. Any feedback or contribution is welcome.

FlowOnRails is a library built to bring Railway Oriented Programming to C# natively and with a focus on developer experience.

## Features

- Zero Allocations. Built with readonly partial structs. Returning successful results does not allocate memory on the heap.
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
        return Rail.Fail(new ValidationError("ID must be positive."));

    var user = Database.Find(id);
    if (user is null)
        return Rail.Fail(new NotFoundError("User not found."));

    return RailFor<User>.Ok(user);
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
    Console.WriteLine($"Failed: {error.Message}");
    return;
}

Console.WriteLine($"Success: {user.Name}");
```

### Async Extensions

FlowOnRails provides extensions like BindAsync, MapAsync and MapErrorAsync for asynchronous operations:

```csharp
var result = await GetUserAsync(id)
    .MapErrorAsync(e => new DomainError($"Failed to fetch user: {e.Message}"))
    .BindAsync(user => ProcessUserAsync(user));
```

## Contributing

Feel free to open an Issue or submit a Pull Request if you find a bug or want to propose improvements.

## License

Distributed under the MIT License. See the LICENSE file for more details.
