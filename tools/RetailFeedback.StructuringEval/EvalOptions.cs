using Microsoft.Extensions.Options;

namespace RetailFeedback.StructuringEval;

/// <summary>
/// Bound from the "Eval" config section; validated at startup. Data paths are
/// resolved against the working directory (run from the repo root); PromptPath
/// falls back to the tool's own output directory since the prompt ships with it.
/// </summary>
public sealed class EvalOptions
{
    public const string SectionName = "Eval";

    public List<string> Candidates { get; init; } = [];
    public int Repetitions { get; init; } = 3;
    public string InputPath { get; init; } = "";
    public string OutputDir { get; init; } = "";
    public string PromptPath { get; init; } = "";

    /// <summary>Suppress "thinking" traces (qwen3 emits them by default) via the
    /// API-level think=false on the client (see ILlmClientFactory).</summary>
    public bool DisableThinking { get; init; } = true;

    public float Temperature { get; init; }

    /// <summary>Cap on generated tokens per call (containment against runaway
    /// output); 0 = no cap, model default.</summary>
    public int MaxOutputTokens { get; init; } = 512;
}

public sealed class EvalOptionsValidator : IValidateOptions<EvalOptions>
{
    public ValidateOptionsResult Validate(string? name, EvalOptions options)
    {
        var failures = new List<string>();

        if (options.Candidates.Count == 0)
            failures.Add("Eval:Candidates must list at least one model.");
        if (options.Candidates.Any(string.IsNullOrWhiteSpace))
            failures.Add("Eval:Candidates must not contain empty model names.");
        if (options.Repetitions is < 1 or > 10)
            failures.Add($"Eval:Repetitions must be within 1..10, got {options.Repetitions}.");
        if (string.IsNullOrWhiteSpace(options.InputPath))
            failures.Add("Eval:InputPath must be set.");
        if (string.IsNullOrWhiteSpace(options.OutputDir))
            failures.Add("Eval:OutputDir must be set.");
        if (string.IsNullOrWhiteSpace(options.PromptPath))
            failures.Add("Eval:PromptPath must be set.");
        if (options.MaxOutputTokens < 0)
            failures.Add($"Eval:MaxOutputTokens must be >= 0 (0 disables the cap), got {options.MaxOutputTokens}.");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
