# FlowOnRails.AspNetCore

FlowOnRails.AspNetCore is the first satellite library for FlowOnRails. It connects
`Rail` and `RailFor<T>` to ASP.NET Core endpoints, letting each endpoint describe
its successful response while configured domain errors become HTTP ProblemDetails.

Visibility rules control which error data may appear in a response. Mappings define
the HTTP status, problem fields and extensions, keeping HTTP configuration in the
application's API layer and out of the domain.

## Install the package

```sh
dotnet add package FlowOnRails.AspNetCore --version 0.1.0
```

The package depends on `FlowOnRails` and includes the generator as a C# analyzer.
No separate generator package or runtime dependency on Roslyn is required. Use the
.NET 10 SDK (Roslyn 5 / C# 14) or a compatible newer compiler.

## Configure the integration

```csharp
using FlowOnRails.AspNetCore.Translation;

builder.Services.AddFlowOnRailsVisibility(policy =>
    policy.Global.ForTree<PublicError>().TranslateEachType());

builder.Services.AddApiProblems(); // Generated from the configuration class below.
```

Register each policy/catalog once. Registration executes configuration methods and
builds immutable snapshots. It does not execute the per-occurrence callbacks.
The low-level `AddFlowOnRailsTranslation`/`TranslationBuilder` API is also available
for explicit registration without generation; generated code uses that same API.

A missing required DI registration is a configuration error. An authorized error
without a mapping uses the generic problem; these are separate situations.

## Describe success in the endpoint

```csharp
// Minimal API, including custom files/text/created/accepted results:
return result.ToHttpResult(value => Results.Ok(value));

// Controller, preserving native MVC execution and output formatters:
return result.ToActionResult(value => Ok(value));

// Rail without a value:
return result.ToHttpResult(() => Results.NoContent());
```

Factories run once on success, during conversion, and are skipped on failure.
They must return a non-null result. `ToActionResult` returns `ActionResult`, usable
as `IActionResult` or through the standard conversion to `ActionResult<T>`.
A custom `IActionResult` is executed using its original MVC ActionContext.

On failure, execution resolves the translator and policy from the current request's
services. Endpoint/group/controller metadata selects the authorized contract. The
first candidate with an exact map is projected; no candidate/map uses the fallback.
Generic configuration receives request data, not the hidden domain error. Without a
custom generic mapping, the response is a neutral 500 with no domain properties.

The HTTP path uses `IProblemDetailsService` if registered and accepted by a writer;
otherwise it writes `application/problem+json` with the HTTP JSON options. This
fallback also applies when no writer accepts the request's media types. The MVC
path uses ObjectResult and MVC formatters. Both support ProblemDetails customization;
application customizers/writers can subsequently change the output. Started
responses cannot be replaced by a domain failure.

## Configure error visibility

Errors use the generic problem by default. Visibility rules determine which error
contract may supply response data; a mapping defines how that contract is translated.

| Selection | Selected errors |
| --- | --- |
| `For<TError>()` | Exactly `TError` |
| `ForDescendantsOf<TError>()` | Descendants, excluding `TError` |
| `ForTree<TError>()` | `TError` and its descendants |

Choose `TranslateEachType()` to use each selected error's own map, or
`UseGenericProblem()` to return the configured generic problem.
`UseNearestAncestorMapping()` searches outside the selected range for an ancestor
whose translation contract is authorized. It exposes that ancestor's configured
properties rather than collecting the concrete error's properties.

Global rules apply across the application. Use `ForController<TController>` for an
exact controller type; derived controllers do not inherit those overrides:

```csharp
builder.Services.AddFlowOnRailsVisibility(policy =>
{
    policy.Global.ForTree<PublicError>().TranslateEachType();
    policy.ForController<UsersController>(scope =>
        scope.For<UserNotFound>().UseGenericProblem());
});
```

For Minimal APIs, apply `WithErrorVisibility` to an endpoint or route group:

```csharp
var users = app.MapGroup("/users")
    .WithErrorVisibility(scope => scope.ForTree<PublicError>().UseGenericProblem());

users.MapGet("/{id:int}", (int id, UserService service) =>
    service.Find(id).ToHttpResult(Results.Ok));
```

Matching rules are considered in this order: endpoint, innermost to outermost route
group, exact controller, then global. A scope with no matching rule falls through
to the next scope. Within a scope, the nearest configured root wins; at the same
root, exact selection takes precedence over descendants, then the whole tree.
Conflicting rules and attempts to reopen a generic exclusion within the same scope
are rejected during configuration.

## Declare mappings in the HTTP layer

The following names represent domain errors from a referenced assembly:

```csharp
[ErrorAssembly(typeof(PublicError))]
public static partial class ApiProblems
{
    [HttpProblem(StatusCodes.Status404NotFound)]
    [AutomaticExtensions]
    [UseDataField]
    private static void MissingUser(ProblemBuilder<UserNotFound> problem)
    {
        problem.Type("https://example.com/problems/user-not-found");
        problem.Title("User not found");
        problem.Detail(error => $"User {error.Id} was not found.");
    }

    [HttpProblem(StatusCodes.Status422UnprocessableEntity)]
    private static void InvalidUser(ProblemBuilder<InvalidUserId> problem)
    {
        problem.Type("https://example.com/problems/invalid-user-id");
        problem.Title("Invalid user identifier");
        problem.Extensions((error, data) => data["invalidId"] = error.Id);
    }

    [GenericProblem] // 500 unless another status is supplied.
    private static void Generic(GenericProblemBuilder problem)
    {
        problem.Type("https://example.com/problems/unexpected");
        problem.Title("Unable to complete the operation");
        problem.Extensions((context, data) => data["requestId"] = context.TraceIdentifier);
    }
}
```

Configuration method names are unrestricted. Methods must be synchronous,
non-generic `static void`, with a body and one by-value `ProblemBuilder<TError>`
parameter (`GenericProblemBuilder` for the fallback). Containers are top-level,
non-generic `static partial` classes; private methods are supported.

Status is required for each exact mapping and must be 400–599. Without configured
fields, `type` is `about:blank`, title is the HTTP reason phrase, and detail/instance
are omitted. Set a problem type URI and title when defining an application-specific
problem. `Detail` and `Instance` can use factories; their values are evaluated for
the current occurrence. Each response gets a new ProblemDetails and extensions.

`ErrorAssembly` can be repeated for multiple domain assemblies. Any accessible
marker type identifies its assembly; it does not select a subtree. The current
assembly is included automatically. Mapping discovery uses compilation symbols,
without loading assemblies at runtime or guessing endpoint return paths. Only
contracts explicitly mapped from selected assemblies are referenced in generated
code; unrelated file-local or obsolete errors do not affect registration.
`Obsolete` does not remove an explicitly configured mapping or projector. Such
references retain normal C# diagnostics: a warning by default, or a compilation
error when `ObsoleteAttribute.IsError` is true.
Only mapped automatic contracts need generated projectors. Closed generic contracts
can appear explicitly in mapping signatures; open generic definitions are not maps.

## Automatic and manual extensions

Automatic projection reads public instance properties with public accessible
getters, including inherited properties of the selected contract. It excludes
fields, static properties and indexers, and preserves null values. Keys are the
original C# member names; `JsonPropertyName` on the original error does not rename
the projected entries. Nested values still use the application's serializer.

Without `UseDataField`, entries are extension members at the root of the problem.
Properties named exactly `type`, `title`, `status`, `detail` or `instance` produce
`FORHTTP007` and are omitted without evaluating their getters. `Status` is a
different name. Projects may promote warnings to errors. With `UseDataField`, the
entries go into a `data` object and those root-name conflicts no longer apply.

Member selection follows normal C# lookup and accessibility at the generated call
site. A visible `new` member hides its base member; an inaccessible member need not.
Virtual getters retain their normal dispatch, including when an ancestor contract
was selected. The generator does not cast to derived types to collect more data.

Manual extensions use the application-chosen keys and values, without extra name
restrictions. Automatic and manual projection cannot be combined in a mapping.
Getter/callback/serializer exceptions and request cancellation propagate; they do
not become domain errors. Complex object serialization remains the application's
responsibility. A non-boxable property, such as a Span, requires manual projection
and is diagnosed rather than producing broken generated code.

## Develop from source

For development directly from source, reference both projects:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/FlowOnRails.AspNetCore.csproj" />
  <ProjectReference Include="path/to/FlowOnRails.AspNetCore.Generators.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

## Try the sample

From the repository root:

```sh
dotnet run --project samples/FlowOnRails.AspNetCore.Sample --urls http://localhost:5080
```

Try these paths:

| Request | Result |
| --- | --- |
| `GET /minimal/users/1` | 200 with a user |
| `GET /minimal/users/2` | 404 with automatic properties under `data`, including `Reason: null` |
| `GET /minimal/users/0` | 422 with manually configured extensions |
| `GET /minimal/hidden/2` | The configured generic 500 without domain properties |
| `GET /mvc/users/1` | Native MVC response; accepts XML when requested |
| `GET /minimal/empty` | 204 without a body |

The sample emits generated sources under `obj`, so its registration and projectors
can also be inspected. See [ApiProblems](https://github.com/Alguem5381/FlowOnRails/blob/main/samples/FlowOnRails.AspNetCore.Sample/ApiProblems.cs)
and [Program](https://github.com/Alguem5381/FlowOnRails/blob/main/samples/FlowOnRails.AspNetCore.Sample/Program.cs).

## Generator diagnostics

| ID | Default severity | Meaning |
| --- | --- | --- |
| FORHTTP001 | Error | Unsupported configuration method signature |
| FORHTTP002 | Error | Unsupported configuration class |
| FORHTTP003 | Error | Orphan/incompatible mapping or projection attributes |
| FORHTTP004 | Error | Error status outside 400–599 |
| FORHTTP005 | Error | Duplicate exact map or generic definition |
| FORHTTP006 | Error | Domain assembly not selected / invalid assembly marker |
| FORHTTP007 | Warning | Reserved automatic root property omitted |
| FORHTTP008 | Error | Automatic property cannot be boxed into extensions |

Duplicate detection uses C# identity conversions rather than type spelling. Maps
for `SomeError<dynamic>` and `SomeError<object>` conflict, as do maps differing
only in tuple element names or nullable reference annotations. Distinct value types
such as `SomeError<int>` and `SomeError<int?>` remain separate contracts.

Configuration-body conflicts, such as manually configuring extensions on a map
marked automatic, are rejected during catalog registration. Arbitrary C# callback
bodies are not executed or fully interpreted by the generator.
