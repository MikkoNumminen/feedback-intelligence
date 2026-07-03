using Microsoft.Extensions.AI;

namespace RetailFeedback.Llm;

/// <summary>
/// The only seam that knows which provider backs <see cref="IChatClient"/>.
/// Application code (including evals) requests clients by model name and never
/// touches a provider SDK — this is what makes "switch to Azure OpenAI" a config
/// change plus an eval run.
/// </summary>
public interface ILlmClientFactory
{
    /// <summary>Creates a client bound to the given model. Caller owns disposal.</summary>
    IChatClient CreateForModel(string model);
}
