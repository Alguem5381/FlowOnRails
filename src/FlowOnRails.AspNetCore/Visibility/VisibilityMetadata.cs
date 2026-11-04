using Microsoft.AspNetCore.Builder;

namespace FlowOnRails.AspNetCore.Visibility;

internal sealed record VisibilityMetadata(IEndpointConventionBuilder Owner, VisibilityIndex Index);
