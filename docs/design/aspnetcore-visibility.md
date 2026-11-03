# ASP.NET Core error visibility

Status: implemented foundation, not yet a published package (2026-11-03).

`FlowOnRails.AspNetCore` is a separate project in this solution. The base library
has no ASP.NET dependency. The satellite currently implements visibility only:
no HTTP status mapping, property projection, ProblemDetails writing, endpoint
success helpers, source generator, analyzer, or OpenAPI support is implemented.

## Separate selection, visibility, and translation

Visibility answers which error contract may be considered for translation. HTTP
mapping answers how that contract becomes a response, including omitted properties.
A visible contract without an HTTP mapping must ultimately use the generic response.
Visibility does not promise that arbitrary values stored in properties are safe.

```csharp
using FlowOnRails.AspNetCore.Visibility;

builder.Services.AddFlowOnRailsVisibility(errors =>
{
    errors.ForTree<PublicError>(tree =>
    {
        tree.TranslateEachType();

        tree.ForDescendantsOf<NotFoundError>()
            .UseNearestAncestorMapping();

        tree.ForTree<InternalError>()
            .UseGenericProblem();
    });

    errors.ForController<UsersController>(controller =>
    {
        controller.ForTree<NotFoundError>()
            .UseGenericProblem();
    });
});
```

Here `PublicError` includes both `NotFoundError` and `InternalError` families.
The first rule authorizes concrete contracts throughout the public family.
The descendants of `NotFoundError` instead search for a translation starting with
`NotFoundError` itself. A translator for that ancestor must project its contract,
never the concrete descendant's extra properties. `InternalError` and its entire
family require the generic response. In `UsersController`, the whole not-found
family requires the generic response instead of using the global behavior.

These rules intentionally use class inheritance, not type-name conventions.
Nothing assumes that a type named `NotFoundError` maps to 404.

## Selection ranges

| Selection | Selected set |
|---|---|
| `For<T>()` | Exactly T |
| `ForDescendantsOf<T>()` | All descendants, excluding T |
| `ForTree<T>()` | T and all descendants |

Tree and descendants selections also accept a configuration block. Blocks limit
all nested selections to their range; they do not change resolution priority.
A block can merely organize narrower rules without assigning a behavior to its root.
Empty blocks have no effect; creating a terminal selection without assigning a
behavior is a configuration error. Descendants-only blocks cannot select their
own excluded root. Unrelated selections are rejected.

## Behaviors and ancestor lookup

| Behavior | Authorized translation candidates |
|---|---|
| `TranslateEachType()` | Only the concrete error type |
| `UseGenericProblem()` | None; resolution ends |
| `UseNearestAncestorMapping()` | Authorized ancestors outside the selected range, nearest first |

For ancestor lookup, an authorized ancestor has effective `TranslateEachType`
visibility. Unconfigured ancestors are skipped. Ancestors with ancestor-lookup
behavior do not authorize their own contracts. An explicit generic-response rule
on an ancestor stops the search, including access to contracts above that boundary.
Already collected nearer candidates remain available.

The selected range determines where ancestor contracts may start:

- `For<T>()`: search from T's parent.
- `ForDescendantsOf<T>()`: exclude descendant contracts; T can be the first candidate.
- `ForTree<T>()`: exclude the entire selected tree; search above T.

A narrower explicit rule may give an individual type its own translation. This
does not make that type a candidate for other errors whose effective ancestor
rule excludes the whole tree. The ancestor search is bounded by the effective
selection for the original error.

`Resolve` returns an `ErrorVisibilityResolution` with the effective behavior and
an immutable ordered `TranslationCandidates` list. It does not check mapping
existence. The future translator must choose the first candidate with an actual
HTTP mapping. If the list is empty or no candidate has a mapping, it must use the
generic response. The planned default is a generic 500 ProblemDetails without
error properties; this core does not write that response yet.

## Deterministic precedence

1. Search rules for the exact selected controller class.
2. If none match the error, search global rules.
3. Within that scope, the nearest matching error ancestor wins.
4. At the same root: exact type, descendants-only, then whole tree.

Registration order has no effect. No matching rule means generic. Controller
configuration is an explicit override: even a broad local rule takes precedence
over a more specific global rule. It can authorize a globally excluded family.
Ancestor resolution uses the same controller scope and global fallback for each
ancestor candidate.

Controller rules are exact: a configured base controller does not grant permission
to subclasses automatically. Other controllers remain isolated. Configuration
accepts controller classes without requiring inheritance from `ControllerBase`.
There are no per-action rules or controller-family rules in this first version.

Same-scope generic subtree exclusions cannot be reopened by a narrower translation
or ancestor rule. Such contradictions fail validation. To exclude descendants
while allowing their root, combine `For<T>().TranslateEachType()` with
`ForDescendantsOf<T>().UseGenericProblem()`. A broad inclusion with a narrower
exclusion is valid. Repeated identical rules are harmless; conflicting behaviors
for the same selection are errors.

## ASP.NET integration and lifetime

```csharp
var policy = httpContext.RequestServices
    .GetRequiredService<ErrorVisibilityPolicy>();

var visibility = policy.ResolveForRequest(error, httpContext);
```

Request resolution reads `ControllerActionDescriptor.ControllerTypeInfo` from the
selected endpoint metadata. It does not inspect route names, activate a controller,
or retain an HttpContext. Minimal APIs and endpoints without controller metadata
use global rules. For resolution without a request:

```csharp
var visibility = policy.Resolve(error.GetType(), typeof(UsersController));
```

The registration builds and validates a singleton snapshot immediately. Later
changes to a retained builder cannot alter it. The resolver does not inspect
properties, call `ToString`, or serialize the error. It selects types only.
Register the policy once; global and controller configurations share that registration.

## Next: mapping validation and generator

The visibility core cannot diagnose a missing 404 mapping before a translation
catalog exists. A later analyzer should distinguish:

- A contract authorized for own translation with no HTTP mapping.
- An ancestor-lookup rule with no mapped authorized ancestor among known errors.
- Intentional generic responses, which do not require a mapping.

The diagnostic must consider controller overrides. It must use configured status
mappings, not guess HTTP status from class names. Runtime configuration validation
is implemented now; compile-time diagnostics require the future analyzer/generator.

References:

- [Controller action metadata](https://source.dot.net/Microsoft.AspNetCore.Mvc.Core/Controllers/ControllerActionDescriptor.cs.html)
- [ASP.NET Core API error handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0)
