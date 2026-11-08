using FlowOnRails.AspNetCore.Translation;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers immutable exact error translations independently of visibility.</summary>
public static class TranslationServiceExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Configures one translation catalog, normally called by generated registration.</summary>
        public IServiceCollection AddFlowOnRailsTranslation(Action<TranslationBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            if (services.Any(descriptor => descriptor.ServiceType == typeof(ProblemTranslator)))
                throw new InvalidOperationException("FlowOnRails translation is already registered.");

            var builder = new TranslationBuilder();

            configure(builder);
            services.AddSingleton(builder.Build());

            return services;
        }
    }
}
