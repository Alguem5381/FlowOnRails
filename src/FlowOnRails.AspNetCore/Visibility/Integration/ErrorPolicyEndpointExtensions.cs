using FlowOnRails.AspNetCore.Visibility;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Configures visibility for an endpoint or route group.</summary>
public static class ErrorPolicyEndpointExtensions
{
    extension<TBuilder>(TBuilder endpoints) where TBuilder : class, IEndpointConventionBuilder
    {
        /// <summary>
        /// Attach an immutable visibility scope. Endpoint rules precede inner groups, outer groups,
        /// controllers and global rules. Configure each endpoint or group once.
        /// </summary>
        public TBuilder WithErrorVisibility(Action<RuleScope> configure)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            ArgumentNullException.ThrowIfNull(configure);

            var builder = new PolicyBuilder();

            configure(builder.Global);

            var index = builder.Build().GlobalIndex;

            endpoints.Add(endpoint =>
            {
                if (endpoint.Metadata.OfType<EndpointScopeMetadata>()
                    .Any(metadata => ReferenceEquals(metadata.Owner, endpoints)))
                    throw new InvalidOperationException(
                        "Visibility is already configured for this endpoint or route group. Combine its rules in one WithErrorVisibility call.");

                endpoint.Metadata.Add(new EndpointScopeMetadata(endpoints, index));
            });

            return endpoints;
        }
    }
}
