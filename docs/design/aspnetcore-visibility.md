# ASP.NET Core error visibility

Status: implemented foundation, not yet a published package (updated 2026-11-04).

`FlowOnRails.AspNetCore` is separate from the base library. It selects which error
contracts may be considered for HTTP translation. Status mappings, property
projection, ProblemDetails writing, success response helpers, source generators,
compile-time analyzers and OpenAPI support are not implemented yet.

## Configure global and controller scopes

```csharp
using FlowOnRails.AspNetCore.Visibility;

builder.Services.AddFlowOnRailsVisibility(visibility =>
{
    visibility.Global.ForTree<PublicError>(tree =>
    {
        tree.TranslateEachType();

        tree.ForDescendantsOf<NotFoundError>()
            .UseNearestAncestorMapping();

        tree.ForTree<InternalError>()
            .UseGenericProblem();
    });

    visibility.ForController<UsersController>(controller =>
    {
        controller.ForTree<NotFoundError>()
            .UseGenericProblem();
    });
});
```

`PublicError` includes the `NotFoundError` and `InternalError` families in this
example. The public family permits concrete contracts; not-found descendants
instead use ancestor contracts beginning with `NotFoundError`. The internal
family requires a generic response. `UsersController` overrides the not-found
family to require a generic response too.

`VisibilityBuilder` organizes configuration and builds the policy. It has a
`Global` scope instead of inheriting selector methods. Controller callbacks also
receive a `VisibilityScope`. A `VisibilityTree` is a bounded specialization of
that scope and adds a default behavior for the selected tree; it still supports
narrower selections without duplicating the selector implementation.

## Select a type or family

| Selection | Selected set |
|---|---|
| `For<T>()` | Exactly T |
| `ForDescendantsOf<T>()` | Descendants, excluding T |
| `ForTree<T>()` | T and all descendants |

Tree and descendants selections also accept configuration blocks. A block bounds
its nested selections; it does not add another precedence level. Blocks may
organize narrower rules without assigning a default behavior. Selecting an
unrelated type or including a descendants-only block's root is rejected.
Empty organizational blocks have no effect. A terminal selection without a
behavior is a configuration error.

## Choose a behavior

| Behavior | Authorized projection contracts |
|---|---|
| `TranslateEachType()` | Only the concrete error type |
| `UseGenericProblem()` | None; stop resolution |
| `UseNearestAncestorMapping()` | Authorized ancestors outside the selected range, nearest first |

An ancestor is authorized when its effective behavior is `TranslateEachType`.
Unconfigured ancestors are skipped; ancestor-lookup behavior does not authorize
its own contract. An explicitly generic ancestor stops lookup above that boundary.
Nearer candidates already collected remain available.

The effective selection for the original error bounds candidate lookup:

- `For<T>()`: begin at T's parent.
- `ForDescendantsOf<T>()`: exclude descendant contracts; T may be the first candidate.
- `ForTree<T>()`: exclude all contracts in T's tree; begin above T.

A narrower own-translation rule can authorize an individual type. It does not
make that type available to another error whose effective rule excludes that
whole tree from ancestor candidates.

Visibility returns a `VisibilityResolution`, containing `Behavior` and an
immutable `TranslationCandidates` list. It does not check whether mappings exist.
The future translator must use the first candidate with a mapping, projecting
that candidate's contract, never additional concrete-descendant properties.
If no candidate or mapping exists, the translator must use the generic response.
The planned default is a generic 500 ProblemDetails, but visibility itself writes
no HTTP response. Nothing infers status codes from names such as `NotFoundError`.

Property selection is a separate mapping concern. Allowing a contract does not
guarantee that arbitrary objects stored in its properties are safe to expose.

## Minimal APIs: endpoint and route-group scopes

```csharp
var users = app.MapGroup("/users")
    .WithErrorVisibility(scope =>
    {
        scope.ForDescendantsOf<NotFoundError>()
            .UseNearestAncestorMapping();
    });

users.MapGet("/{id}", GetUser)
    .WithErrorVisibility(scope =>
    {
        scope.ForTree<InternalError>()
            .UseGenericProblem();
    });
```

`WithErrorVisibility` works on endpoints and route groups. Configure each endpoint
or group once, placing all its rules in one callback. Configuration is validated
and snapshotted immediately. Duplicate calls on the same builder are rejected
when its endpoints are constructed, rather than silently depending on call order.
Endpoints inherit their groups' metadata; sibling routes do not inherit rules
specific to another endpoint. Nested groups can introduce narrower local scopes.

Register `AddFlowOnRailsVisibility` once even when only Minimal APIs need local
rules; an empty global configuration is valid. Endpoint/group configuration does
not register another policy or use a process-wide static registry.

## Resolution order

For each lookup, including ancestor candidate lookup:

1. The selected endpoint's scope.
2. Route-group scopes, innermost to outermost.
3. The exact controller's scope, if controller metadata exists.
4. The global scope.

Only a scope with no matching rule falls through. A generic-response rule is a
matching decision, not absence of configuration. A local broad rule therefore
precedes a global specific rule. Local scopes can explicitly authorize a family
excluded globally or by an outer group.

Within one scope, the nearest matching error root wins. At that root, priority
is exact type, descendants-only, then whole tree. Registration order does not
change that order. Group nesting also determines precedence regardless of the
order in which different groups were configured.

Rules for base controllers are not inherited by derived controllers. Controller
classes need not inherit `ControllerBase`. Endpoint metadata rules can coexist
with controller rules and have higher priority. There is no separately named
controller-action configuration API in this version.

## Conflict validation

Within the same scope, explicit own-translation or ancestor-lookup rules cannot
reopen a generic subtree exclusion. Conflicts are configuration errors. A broad
inclusion with a narrower exclusion is valid, including a whole-tree inclusion
restricted by a descendants-only exclusion at the same root. The root can remain
visible while all descendants are generic.

Exact-type generic exclusions do not exclude descendants. Identical duplicate
rules are harmless; contradictory behaviors for the same selection are errors.
These same-scope checks do not prohibit deliberate overrides in a more local scope.

## Indexed policy and request lifetime

`Build()` validates and copies the rules, then prepares frozen dictionary indexes:
controller type to scope index, and error root type to fixed exact/descendants/tree
slots within each scope. Endpoint and group scopes get equivalent immutable
indexes when configured.

Runtime resolution follows the inheritance chain and performs indexed lookups.
It does not filter or sort the full rule collection on each request. Ancestor
resolution can still walk inheritance chains more than once; this change removes
dependence on the total number of unrelated rules, not all traversal or allocation.
There is no resolution cache and no claimed benchmark speedup.

```csharp
var policy = httpContext.RequestServices
    .GetRequiredService<VisibilityPolicy>();

var resolution = policy.ResolveForRequest(error, httpContext);
```

Request resolution reads the selected endpoint's visibility metadata and
`ControllerActionDescriptor.ControllerTypeInfo`, without activating controllers
or retaining an HttpContext. An endpoint without local or controller metadata
uses global rules. Without a request, resolve global/controller rules directly:

```csharp
var resolution = policy.Resolve(error.GetType(), typeof(UsersController));
```

The registered policy is a per-container singleton, not static state. Separate
applications have independent policies. Builder and scope changes after snapshot
creation do not change an existing policy. The resolver reads types only, not
properties or `ToString`. The base FlowOnRails assembly has no ASP.NET dependency.

## Next: HTTP mapping and analyzer

The visibility core cannot diagnose a missing HTTP mapping before a translation
catalog exists. A future analyzer should distinguish an authorized own contract
with no mapping, an ancestor rule with no mapped authorized ancestor, and an
intentional generic response (which requires no specific mapping). It must take
all local scopes into account and use configured mappings rather than type names.

References:

- [ASP.NET Core route groups and metadata](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-10.0)
- [FrozenDictionary](https://learn.microsoft.com/en-us/dotnet/api/system.collections.frozen.frozendictionary-2?view=net-10.0)
