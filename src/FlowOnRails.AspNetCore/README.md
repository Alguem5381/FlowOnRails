# FlowOnRails.AspNetCore

ASP.NET Core integration for FlowOnRails, under development in this solution.
This project is not yet packaged or published.

The first implemented component is error visibility: select types, descendants,
or trees; authorize concrete contracts, use authorized ancestors, or require a
generic response. Global, exact-controller, endpoint and nested route-group rules are resolved
deterministically using immutable indexes. Configure shared rules through
`builder.Global` and Minimal API scopes through `WithErrorVisibility`.
HTTP mappings and response helpers will build on this policy.

See [the visibility design and examples](../../docs/design/aspnetcore-visibility.md)
for configuration, precedence, controller scoping, and remaining work.
