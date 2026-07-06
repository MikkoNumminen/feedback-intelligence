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

    /// <summary>Cap on the per-item alert SCREEN — one strict yes/no LLM call per
    /// keyword-less complaint. Poro floods when selecting alerts from a list but
    /// discriminates reliably on one item, so every candidate is judged alone.
    /// These calls are tiny (one-word answer) and sit on their OWN budget, so the
    /// safety screen never starves narrative synthesis. Set high enough to cover
    /// every keyword-less complaint in a window. See ADR-0015 ("Poro tuning").</summary>
    public int MaxAlertVerifyCalls { get; init; } = 80;

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

    /// <summary>Minimum items in a category group before ANY non-stable trend is
    /// reported. Below this a first/second-half split is noise, not signal — the
    /// report says "stable" (vakaa) rather than invent a direction.</summary>
    public int MinItemsForTrend { get; init; } = 6;

    /// <summary>Significance threshold, in standard deviations, that the
    /// first/second-half VOLUME split must clear before a trend is reported. Under
    /// uniform-in-time arrivals the second-half count is ~Binomial(n, 0.5), so the
    /// (second − first) gap has sd √n; a trend needs |second − first| ≥ z·√n.
    /// Higher = stricter: fewer hallucinated trends on organic noise, at the cost
    /// of weak real trends reading as "stable" (the safe, honest failure). Measured
    /// against organic noise and canonical story shapes — see ADR-0017.</summary>
    public double TrendSignificanceZ { get; init; } = 1.6;
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
        if (options.MaxAlertVerifyCalls < 0)
            failures.Add($"Report:MaxAlertVerifyCalls must be >= 0 (0 = no verify pass), got {options.MaxAlertVerifyCalls}.");
        if (options.ExcerptChars < 20)
            failures.Add($"Report:ExcerptChars must be >= 20, got {options.ExcerptChars}.");
        if (options.DefaultWindowDays < 1 || options.MaxWindowDays < options.DefaultWindowDays)
            failures.Add($"Report window days must satisfy 1 <= default <= max, got {options.DefaultWindowDays}/{options.MaxWindowDays}.");
        if (options.MaxItemsPerWindow < 1)
            failures.Add($"Report:MaxItemsPerWindow must be positive, got {options.MaxItemsPerWindow}.");
        if (options.MinItemsForTrend < 3)
            failures.Add($"Report:MinItemsForTrend must be >= 3 (a half-split needs a few items), got {options.MinItemsForTrend}.");
        if (options.TrendSignificanceZ < 0)
            failures.Add($"Report:TrendSignificanceZ must be >= 0, got {options.TrendSignificanceZ}.");
        if (options.MaxLlmCallsPerReport < 0)
            failures.Add($"Report:MaxLlmCallsPerReport must be >= 0 (0 = deterministic only), got {options.MaxLlmCallsPerReport}.");
        if (options.ReportCacheSeconds < 0)
            failures.Add($"Report:ReportCacheSeconds must be >= 0 (0 = off), got {options.ReportCacheSeconds}.");
        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
