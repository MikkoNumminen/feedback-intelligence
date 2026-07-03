namespace RetailFeedback.Llm;

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
}
