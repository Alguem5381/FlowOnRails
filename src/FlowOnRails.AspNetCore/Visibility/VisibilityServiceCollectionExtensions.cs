using FlowOnRails.AspNetCore.Visibility;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the ASP.NET Core error visibility policy.</summary>
public static class VisibilityServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Configure and validate a singleton visibility snapshot. This does not register HTTP
        /// translators, exception handlers, or ProblemDetails writers. Register visibility once.
        /// </summary>
        public IServiceCollection AddFlowOnRailsVisibility(Action<ErrorVisibilityBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            if (services.Any(descriptor => descriptor.ServiceType == typeof(ErrorVisibilityPolicy)))
                throw new InvalidOperationException(
                    "FlowOnRails visibility is already registered. Configure global and controller rules in the same registration.");

            var builder = new ErrorVisibilityBuilder();

            configure(builder);
            services.AddSingleton(builder.Build());

            return services;
        }
    }
}
