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
    /// <summary>
    /// Creates a client bound to the given model. Caller owns disposal.
    /// <paramref name="disableReasoning"/> suppresses thinking traces at the
    /// API level (reasoning models only; a no-op for others) — measured
    /// necessity: prompt-level soft switches are not honored on Ollama's
    /// native chat path and thinking silently consumes the output-token budget.
    /// </summary>
    IChatClient CreateForModel(string model, bool disableReasoning = false);
}
