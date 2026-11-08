using Microsoft.AspNetCore.Builder;

namespace FlowOnRails.AspNetCore.Visibility;

internal sealed record EndpointScopeMetadata(IEndpointConventionBuilder Owner, RuleIndex Index);
