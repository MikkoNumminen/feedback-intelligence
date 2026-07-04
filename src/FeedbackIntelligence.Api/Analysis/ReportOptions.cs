using Microsoft.Extensions.Options;

namespace FeedbackIntelligence.Api.Analysis;

/// <summary>Bound from "Report"; validated at startup.</summary>
public sealed class ReportOptions
{
    public const string SectionName = "Report";

    public string SnapshotDir { get; init; } = "data/snapshots";

    // The synthesis and alert-nomination prompts are domain-voiced (persona,
    // language) and live in the active domain module, resolved via IActiveDomain —
    // not here. Adding a domain never edits this neutral config.

    public float SynthesisTemperature { get; init; } = 0.3f;
    public int SynthesisMaxOutputTokens { get; init; } = 700;

    /// <summary>Cap on items listed to the LLM per call (containment; the
    /// aggregation itself is unbounded and deterministic).</summary>
    public int MaxItemsPerLlmCall { get; init; } = 60;

    /// <summary>Excerpt length for alert rows and LLM item lists.</summary>
    public int ExcerptChars { get; init; } = 120;

    public bool AlertNominationEnabled { get; init; } = true;

    /// <summary>Hard cap on LLM calls per report generation — one refresh must
    /// never monopolize the shared GPU gate and starve the desk path. Groups
    /// beyond the budget get deterministic fallbacks (counted, logged).</summary>
    public int MaxLlmCallsPerReport { get; init; } = 8;

    /// <summary>Same-window reports are served from cache this long; ingest
    /// invalidates the cache so a new desk entry shows on the next refresh.</summary>
    public int ReportCacheSeconds { get; init; } = 60;

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
        if (options.MaxLlmCallsPerReport < 0)
            failures.Add($"Report:MaxLlmCallsPerReport must be >= 0 (0 = deterministic only), got {options.MaxLlmCallsPerReport}.");
        if (options.ReportCacheSeconds < 0)
            failures.Add($"Report:ReportCacheSeconds must be >= 0 (0 = off), got {options.ReportCacheSeconds}.");
        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
