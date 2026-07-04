using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FeedbackIntelligence.Core.Domain;

namespace FeedbackIntelligence.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>DI key for the structuring-role <see cref="IChatClient"/>.</summary>
    public const string StructuringKey = "structuring";

    /// <summary>DI key for the synthesis-role <see cref="IChatClient"/>.</summary>
    public const string SynthesisKey = "synthesis";

    public static IServiceCollection AddFeedbackIntelligenceLlm(this IServiceCollection services, IConfiguration configuration)
    {
        // Structuring reads its taxonomy from the active domain; register it here
        // so every LLM host has one (idempotent if the host also registers it).
        services.AddActiveDomain(configuration);

        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LlmOptions>, LlmOptionsValidator>();

        services.AddSingleton<ILlmClientFactory, OllamaLlmClientFactory>();

        services.AddKeyedSingleton<IChatClient>(StructuringKey, static (sp, _) =>
        {
            var models = sp.GetRequiredService<IOptions<LlmOptions>>().Value.Models;
            return sp.GetRequiredService<ILlmClientFactory>()
                .CreateForModel(models.Structuring, models.StructuringDisableReasoning);
        });

        services.AddKeyedSingleton<IChatClient>(SynthesisKey, static (sp, _) =>
        {
            var models = sp.GetRequiredService<IOptions<LlmOptions>>().Value.Models;
            return sp.GetRequiredService<ILlmClientFactory>()
                .CreateForModel(models.Synthesis, models.SynthesisDisableReasoning);
        });

        services.AddOptions<Structuring.StructuringOptions>()
            .Bind(configuration.GetSection(Structuring.StructuringOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<Structuring.StructuringOptions>, Structuring.StructuringOptionsValidator>();
        services.AddSingleton<Structuring.IStructuringService, Structuring.LlmStructuringService>();

        return services;
    }
}
