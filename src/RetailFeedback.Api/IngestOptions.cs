using Microsoft.Extensions.Options;

namespace RetailFeedback.Api;

/// <summary>
/// Containment configuration, mirroring the values measured in the
/// mikkonumminen.dev RAG (see CLAUDE.md "reuse" notes). Bound from "Ingest",
/// validated at startup. Containment is architectural, not prompt-wording: a
/// bounded input cannot smuggle a giant payload past the model.
/// </summary>
public sealed class IngestOptions
{
    public const string SectionName = "Ingest";

    /// <summary>Hard cap on feedback text length, enforced BEFORE any LLM work.</summary>
    public int InputMaxChars { get; init; } = 800;

    /// <summary>Kestrel request-body byte cap.</summary>
    public int MaxBodyBytes { get; init; } = 16384;

    public int RateLimitRequests { get; init; } = 30;
    public double RateLimitWindowSeconds { get; init; } = 60;

    /// <summary>One local GPU serves generation: bound concurrent LLM requests and
    /// SHED (503) after a short wait rather than queue — a queue behind a slow
    /// generation stacks timeouts.</summary>
    public int LlmMaxConcurrency { get; init; } = 2;
    public int LlmAcquireTimeoutMs { get; init; } = 500;

    public string DbPath { get; init; } = "data/feedback.db";
    public string AlertKeywordsPath { get; init; } = "config/alert-keywords.json";

    public List<string> AllowedSources { get; init; } = ["google_review", "email", "web_form", "desk"];
}

public sealed class IngestOptionsValidator : IValidateOptions<IngestOptions>
{
    public ValidateOptionsResult Validate(string? name, IngestOptions options)
    {
        var failures = new List<string>();
        if (options.InputMaxChars < 1)
            failures.Add($"Ingest:InputMaxChars must be positive, got {options.InputMaxChars}.");
        if (options.MaxBodyBytes < options.InputMaxChars)
            failures.Add($"Ingest:MaxBodyBytes ({options.MaxBodyBytes}) must be >= InputMaxChars ({options.InputMaxChars}).");
        if (options.RateLimitRequests < 1)
            failures.Add($"Ingest:RateLimitRequests must be positive, got {options.RateLimitRequests}.");
        if (options.RateLimitWindowSeconds <= 0)
            failures.Add($"Ingest:RateLimitWindowSeconds must be positive, got {options.RateLimitWindowSeconds}.");
        if (options.LlmMaxConcurrency < 1)
            failures.Add($"Ingest:LlmMaxConcurrency must be positive, got {options.LlmMaxConcurrency}.");
        // Must be > 0: an acquire timeout of 0 always sheds even with a free slot.
        if (options.LlmAcquireTimeoutMs < 1)
            failures.Add($"Ingest:LlmAcquireTimeoutMs must be positive, got {options.LlmAcquireTimeoutMs}.");
        if (string.IsNullOrWhiteSpace(options.DbPath))
            failures.Add("Ingest:DbPath must be set.");
        if (string.IsNullOrWhiteSpace(options.AlertKeywordsPath))
            failures.Add("Ingest:AlertKeywordsPath must be set.");
        if (options.AllowedSources.Count == 0 || options.AllowedSources.Any(string.IsNullOrWhiteSpace))
            failures.Add("Ingest:AllowedSources must be a non-empty list of source names.");
        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
