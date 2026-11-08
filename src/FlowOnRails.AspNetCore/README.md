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

Implementation files are grouped into `Configuration`, `Rules`, `Resolution` and
`Integration`. The public namespace stays `FlowOnRails.AspNetCore.Visibility`;
main entry points are `PolicyBuilder`, `RuleScope` and `ErrorPolicy`.

For an explanation of ASP.NET's endpoint builders and our integration, see
[endpoint conventions](../../docs/aspnetcore-endpoint-conventions.md).
