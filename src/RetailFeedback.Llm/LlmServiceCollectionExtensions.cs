using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace RetailFeedback.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>DI key for the structuring-role <see cref="IChatClient"/>.</summary>
    public const string StructuringKey = "structuring";

    /// <summary>DI key for the synthesis-role <see cref="IChatClient"/>.</summary>
    public const string SynthesisKey = "synthesis";

    public static IServiceCollection AddRetailFeedbackLlm(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection(LlmOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LlmOptions>, LlmOptionsValidator>();

        services.AddSingleton<ILlmClientFactory, OllamaLlmClientFactory>();

        services.AddKeyedSingleton<IChatClient>(StructuringKey, static (sp, _) =>
            sp.GetRequiredService<ILlmClientFactory>()
                .CreateForModel(sp.GetRequiredService<IOptions<LlmOptions>>().Value.Models.Structuring));

        services.AddKeyedSingleton<IChatClient>(SynthesisKey, static (sp, _) =>
            sp.GetRequiredService<ILlmClientFactory>()
                .CreateForModel(sp.GetRequiredService<IOptions<LlmOptions>>().Value.Models.Synthesis));

        return services;
    }
}
