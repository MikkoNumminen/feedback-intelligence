using Microsoft.Extensions.Options;

namespace FeedbackIntelligence.Generator;

/// <summary>Bound from the "Generator" config section; validated at startup.
/// Story definitions are domain DATA, loaded at runtime from the active domain
/// module (<c>domains/&lt;active&gt;/stories.json</c>) — not from this config —
/// and validated against the domain taxonomy by <see cref="StoryLibrary"/>.</summary>
public sealed class GeneratorOptions
{
    public const string SectionName = "Generator";

    public string CorePath { get; init; } = "data/corpus/core.jsonl";
    public string VariantsPath { get; init; } = "data/corpus/variants.jsonl";
    public string OutputDir { get; init; } = "data/corpus";

    /// <summary>Newest date in any generated set — FIXED for determinism; the
    /// generate path must never read the clock.</summary>
    public string AnchorDate { get; init; } = "2026-07-01";

    public int NoiseCount { get; init; } = 60;
    public int NoiseWindowDays { get; init; } = 21;

    /// <summary>Time-of-day window for generated timestamps (store feedback hours).
    /// One policy for stories AND noise — divergent hour distributions would leak
    /// story membership.</summary>
    public int DayStartHour { get; init; } = 8;
    public int DayEndHour { get; init; } = 22;

    /// <summary>Bump applied when a sequenced step collides with its predecessor's
    /// timestamp; strict monotonicity is enforced, overflow past the window fails loudly.</summary>
    public int SequenceCollisionGapMinMinutes { get; init; } = 10;
    public int SequenceCollisionGapMaxMinutes { get; init; } = 120;

    public int VariantsPerItem { get; init; } = 6;

    /// <summary>Story-tagged items multiply less (arc-intensity protection,
    /// Mikko 2026-07-03); 0 = originals only, no LLM call for story items.</summary>
    public int StoryVariantsPerItem { get; init; } = 2;

    public string VariantsPromptPath { get; init; } = "prompts/variants-v0.txt";

    /// <summary>Dedicated intensity-preserving prompt for story-tagged items.</summary>
    public string VariantsStoryPromptPath { get; init; } = "prompts/variants-story-v0.txt";

    public float VariantsTemperature { get; init; } = 0.8f;
    public int VariantsMaxOutputTokens { get; init; } = 1024;

    /// <summary>Planted stories for the active domain. Empty from config; the
    /// generate/variants runners populate it from the active domain module.</summary>
    public List<StoryConfig> Stories { get; set; } = [];

    /// <summary>The active domain's ingest channels, populated from the domain
    /// module at runtime. Used only as the source for a noise item that declares
    /// none — so noise never gets a channel that doesn't belong to the domain.</summary>
    public List<string> Sources { get; set; } = [];
}

public sealed class StoryConfig
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Category { get; init; } = "";
    public List<string> ThemeKeywords { get; init; } = [];
    public List<string> Sources { get; init; } = [];
    public int WindowDays { get; init; }
    public int Count { get; init; }
    public string Trend { get; init; } = "";
    public int MinGroundedIds { get; init; }
    public bool ExpectAlert { get; init; }
}

public sealed class GeneratorOptionsValidator : IValidateOptions<GeneratorOptions>
{
    public ValidateOptionsResult Validate(string? name, GeneratorOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.CorePath))
            failures.Add("Generator:CorePath must be set.");
        if (string.IsNullOrWhiteSpace(options.VariantsPath))
            failures.Add("Generator:VariantsPath must be set.");
        if (string.IsNullOrWhiteSpace(options.OutputDir))
            failures.Add("Generator:OutputDir must be set.");
        if (!DateOnly.TryParseExact(options.AnchorDate, "yyyy-MM-dd", out _))
            failures.Add($"Generator:AnchorDate must be yyyy-MM-dd, got '{options.AnchorDate}'.");
        if (options.NoiseCount < 0)
            failures.Add($"Generator:NoiseCount must be >= 0, got {options.NoiseCount}.");
        if (options.NoiseWindowDays < 1)
            failures.Add($"Generator:NoiseWindowDays must be >= 1, got {options.NoiseWindowDays}.");
        if (options.VariantsPerItem is < 1 or > 20)
            failures.Add($"Generator:VariantsPerItem must be within 1..20, got {options.VariantsPerItem}.");
        if (options.StoryVariantsPerItem is < 0 or > 20)
            failures.Add($"Generator:StoryVariantsPerItem must be within 0..20 (0 = originals only), got {options.StoryVariantsPerItem}.");
        if (options.StoryVariantsPerItem > 0 && string.IsNullOrWhiteSpace(options.VariantsStoryPromptPath))
            failures.Add("Generator:VariantsStoryPromptPath must be set when StoryVariantsPerItem > 0.");
        if (options.DayStartHour is < 0 or > 23 || options.DayEndHour is < 1 or > 24 || options.DayStartHour >= options.DayEndHour)
            failures.Add($"Generator:DayStartHour/DayEndHour must satisfy 0 <= start < end <= 24, got {options.DayStartHour}/{options.DayEndHour}.");
        if (options.SequenceCollisionGapMinMinutes < 1
            || options.SequenceCollisionGapMaxMinutes <= options.SequenceCollisionGapMinMinutes)
            failures.Add($"Generator:SequenceCollisionGap minutes must satisfy 1 <= min < max, got {options.SequenceCollisionGapMinMinutes}/{options.SequenceCollisionGapMaxMinutes}.");

        // Stories are NOT validated here — they come from the active domain module
        // at runtime, so their taxonomy check needs the domain descriptor. See
        // StoryLibrary.Load, invoked by the runners.

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
