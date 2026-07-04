using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FeedbackIntelligence.Core.Domain;

public static class DomainServiceCollectionExtensions
{
    /// <summary>Registers the config-selected active domain module. The domain
    /// is loaded once and validated at startup; every mechanism reads its
    /// taxonomy from <see cref="IActiveDomain"/> — nothing hardcodes a domain.
    /// Idempotent: safe to call from several composition roots (the LLM layer
    /// registers it so structuring always has a domain).</summary>
    public static IServiceCollection AddActiveDomain(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DomainOptions>()
            .Bind(configuration.GetSection(DomainOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DomainOptions>, DomainOptionsValidator>());
        services.TryAddSingleton<IActiveDomain, ActiveDomain>();
        return services;
    }
}
