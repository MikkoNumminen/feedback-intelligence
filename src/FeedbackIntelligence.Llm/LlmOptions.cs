namespace FeedbackIntelligence.Llm;

/// <summary>Bound from the "Llm" config section; validated at startup by <see cref="LlmOptionsValidator"/>.</summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>
    /// Currently only "ollama". A new provider (e.g. Azure OpenAI) arrives as a new
    /// accepted value plus an <see cref="ILlmClientFactory"/> implementation — callers
    /// never change.
    /// </summary>
    public string Provider { get; init; } = "";

    public string BaseUrl { get; init; } = "";

    public LlmModelOptions Models { get; init; } = new();
}

/// <summary>Structuring and synthesis are independently configurable models by design.</summary>
public sealed class LlmModelOptions
{
    public string Structuring { get; init; } = "";
    public string Synthesis { get; init; } = "";

    /// <summary>Suppress reasoning traces on the structuring client (API-level
    /// think=false; a no-op for non-reasoning models). Default on — measured
    /// safe for both candidate models in the 2026-07-03 placeholder runs.</summary>
    public bool StructuringDisableReasoning { get; init; } = true;

    /// <summary>Same for the synthesis client. Default off: synthesis quality
    /// may benefit from reasoning if a reasoning model is ever configured.</summary>
    public bool SynthesisDisableReasoning { get; init; }
}
