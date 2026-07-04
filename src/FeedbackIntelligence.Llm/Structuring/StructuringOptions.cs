using Microsoft.Extensions.Options;

namespace FeedbackIntelligence.Llm.Structuring;

/// <summary>Bound from the "Structuring" config section; validated at startup.</summary>
public sealed class StructuringOptions
{
    public const string SectionName = "Structuring";

    /// <summary>Resolved against the working directory, falling back to the
    /// binary's own directory (the prompt ships with the application).</summary>
    public string PromptPath { get; init; } = "prompts/structuring-v0.txt";

    public float Temperature { get; init; }

    /// <summary>Containment cap on generated tokens per call; 0 = no cap.</summary>
    public int MaxOutputTokens { get; init; } = 512;
}

public sealed class StructuringOptionsValidator : IValidateOptions<StructuringOptions>
{
    public ValidateOptionsResult Validate(string? name, StructuringOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.PromptPath))
            failures.Add("Structuring:PromptPath must be set.");
        if (options.MaxOutputTokens < 0)
            failures.Add($"Structuring:MaxOutputTokens must be >= 0 (0 disables the cap), got {options.MaxOutputTokens}.");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
