using Microsoft.Extensions.Options;

namespace RetailFeedback.Api.Analysis;

/// <summary>Bound from "Report"; validated at startup.</summary>
public sealed class ReportOptions
{
    public const string SectionName = "Report";

    public string SnapshotDir { get; init; } = "data/snapshots";
    public string SynthesisPromptPath { get; init; } = "prompts/synthesis-v0.txt";
    public string AlertNominationPromptPath { get; init; } = "prompts/alert-nomination-v0.txt";

    public float SynthesisTemperature { get; init; } = 0.3f;
    public int SynthesisMaxOutputTokens { get; init; } = 700;

    /// <summary>Cap on items listed to the LLM per call (containment; the
    /// aggregation itself is unbounded and deterministic).</summary>
    public int MaxItemsPerLlmCall { get; init; } = 60;

    /// <summary>Excerpt length for alert rows and LLM item lists.</summary>
    public int ExcerptChars { get; init; } = 120;

    public bool AlertNominationEnabled { get; init; } = true;

    public int DefaultWindowDays { get; init; } = 7;
    public int MaxWindowDays { get; init; } = 92;
    public int MaxItemsPerWindow { get; init; } = 2000;
}

public sealed class ReportOptionsValidator : IValidateOptions<ReportOptions>
{
    public ValidateOptionsResult Validate(string? name, ReportOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.SnapshotDir))
            failures.Add("Report:SnapshotDir must be set.");
        if (string.IsNullOrWhiteSpace(options.SynthesisPromptPath))
            failures.Add("Report:SynthesisPromptPath must be set.");
        if (options.AlertNominationEnabled && string.IsNullOrWhiteSpace(options.AlertNominationPromptPath))
            failures.Add("Report:AlertNominationPromptPath must be set when nomination is enabled.");
        if (options.SynthesisMaxOutputTokens < 0)
            failures.Add($"Report:SynthesisMaxOutputTokens must be >= 0, got {options.SynthesisMaxOutputTokens}.");
        if (options.MaxItemsPerLlmCall < 1)
            failures.Add($"Report:MaxItemsPerLlmCall must be positive, got {options.MaxItemsPerLlmCall}.");
        if (options.ExcerptChars < 20)
            failures.Add($"Report:ExcerptChars must be >= 20, got {options.ExcerptChars}.");
        if (options.DefaultWindowDays < 1 || options.MaxWindowDays < options.DefaultWindowDays)
            failures.Add($"Report window days must satisfy 1 <= default <= max, got {options.DefaultWindowDays}/{options.MaxWindowDays}.");
        if (options.MaxItemsPerWindow < 1)
            failures.Add($"Report:MaxItemsPerWindow must be positive, got {options.MaxItemsPerWindow}.");
        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
