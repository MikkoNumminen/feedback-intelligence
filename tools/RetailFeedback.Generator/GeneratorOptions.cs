using Microsoft.Extensions.Options;
using RetailFeedback.Domain.Structuring;

namespace RetailFeedback.Generator;

/// <summary>Bound from the "Generator" config section; validated at startup.
/// Story definitions live fully in appsettings (config over hardcoding).</summary>
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

    public int VariantsPerItem { get; init; } = 6;

    /// <summary>Story-tagged items multiply less (arc-intensity protection,
    /// Mikko 2026-07-03); 0 = originals only, no LLM call for story items.</summary>
    public int StoryVariantsPerItem { get; init; } = 2;

    public string VariantsPromptPath { get; init; } = "prompts/variants-v0.txt";

    /// <summary>Dedicated intensity-preserving prompt for story-tagged items.</summary>
    public string VariantsStoryPromptPath { get; init; } = "prompts/variants-story-v0.txt";

    public float VariantsTemperature { get; init; } = 0.8f;
    public int VariantsMaxOutputTokens { get; init; } = 1024;

    public List<StoryConfig> Stories { get; init; } = [];
}

public sealed class StoryConfig
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Department { get; init; } = "";
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
    private static readonly IReadOnlySet<string> Trends = new HashSet<string>(StringComparer.Ordinal) { "worsening", "stable" };

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
        if (string.IsNullOrWhiteSpace(options.VariantsStoryPromptPath))
            failures.Add("Generator:VariantsStoryPromptPath must be set.");
        if (options.Stories.Count == 0)
            failures.Add("Generator:Stories must define at least one planted story.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var story in options.Stories)
        {
            var label = $"Generator:Stories['{story.Id}']";
            if (string.IsNullOrWhiteSpace(story.Id) || !ids.Add(story.Id))
                failures.Add($"{label}: id must be set and unique.");
            if (!StructuringSchema.Departments.Contains(story.Department))
                failures.Add($"{label}: department '{story.Department}' is not a schema enum value.");
            if (story.ThemeKeywords.Count == 0)
                failures.Add($"{label}: themeKeywords must be a non-empty keyword set.");
            if (story.Sources.Count == 0)
                failures.Add($"{label}: sources must be non-empty.");
            if (story.WindowDays < 1)
                failures.Add($"{label}: windowDays must be >= 1.");
            if (story.Count < 1)
                failures.Add($"{label}: count must be >= 1.");
            if (!Trends.Contains(story.Trend))
                failures.Add($"{label}: trend must be one of [{string.Join(", ", Trends)}].");
            if (story.MinGroundedIds < 1 || story.MinGroundedIds > story.Count)
                failures.Add($"{label}: minGroundedIds must be within 1..count.");
        }

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
