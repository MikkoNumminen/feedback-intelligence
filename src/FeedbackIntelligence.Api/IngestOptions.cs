using Microsoft.Extensions.Options;

namespace FeedbackIntelligence.Api;

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

    /// <summary>Loopback callers (corpus pushes, local tooling, the dev loop)
    /// are exempt from rate limiting — the RAG-measured lesson ("never accept
    /// throttled data as variance"), re-measured here when a 32-item corpus
    /// push could not fit a 30/60s window. Tunnel traffic keeps its real
    /// client IP via forwarded headers and stays limited.</summary>
    public bool RateLimitExemptLoopback { get; init; } = true;

    /// <summary>One local GPU serves generation: bound concurrent LLM requests and
    /// SHED (503) after a short wait rather than queue — a queue behind a slow
    /// generation stacks timeouts.</summary>
    public int LlmMaxConcurrency { get; init; } = 2;
    public int LlmAcquireTimeoutMs { get; init; } = 500;

    public string DbPath { get; init; } = "data/feedback.db";

    public List<string> AllowedSources { get; init; } = ["google_review", "email", "web_form", "desk"];

    public int IdMaxLength { get; init; } = 100;
    public int QueryDefaultLimit { get; init; } = 200;
    public int QueryMaxLimit { get; init; } = 1000;

    /// <summary>Health probes a 1-token real completion; a cold model load can
    /// take tens of seconds, so this is tunable without a recompile.</summary>
    public int HealthTimeoutSeconds { get; init; } = 10;

    /// <summary>Origins allowed to call the API cross-origin (the static-host
    /// frontend, e.g. the Azure SWA URL). Empty = same-origin only, no CORS.</summary>
    public List<string> AllowedCorsOrigins { get; init; } = [];
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
        if (options.AllowedSources.Count == 0 || options.AllowedSources.Any(string.IsNullOrWhiteSpace))
            failures.Add("Ingest:AllowedSources must be a non-empty list of source names.");
        if (options.IdMaxLength < 1)
            failures.Add($"Ingest:IdMaxLength must be positive, got {options.IdMaxLength}.");
        if (options.QueryDefaultLimit < 1 || options.QueryMaxLimit < options.QueryDefaultLimit)
            failures.Add($"Ingest:QueryDefaultLimit/QueryMaxLimit must satisfy 1 <= default <= max, got {options.QueryDefaultLimit}/{options.QueryMaxLimit}.");
        if (options.HealthTimeoutSeconds < 1)
            failures.Add($"Ingest:HealthTimeoutSeconds must be positive, got {options.HealthTimeoutSeconds}.");
        foreach (var origin in options.AllowedCorsOrigins)
        {
            // Exact string matching against the browser's Origin header, which
            // never carries a path or trailing slash — a pasted-from-portal
            // "https://x/" would silently never match.
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || uri.AbsolutePath != "/"
                || origin.EndsWith('/'))
                failures.Add($"Ingest:AllowedCorsOrigins entry '{origin}' must be an absolute http(s) origin with no path and no trailing slash.");
        }
        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
