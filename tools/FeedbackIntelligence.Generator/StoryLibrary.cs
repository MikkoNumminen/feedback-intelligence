using System.Text.Json;
using FeedbackIntelligence.Core.Domain;

namespace FeedbackIntelligence.Generator;

/// <summary>Loads the planted-story definitions from the active domain module
/// (<c>domains/&lt;active&gt;/stories.json</c>) and validates them against the
/// domain taxonomy. Stories are domain DATA, not generator config — switching
/// <c>Domain:Active</c> swaps the whole story set.</summary>
public static class StoryLibrary
{
    private static readonly IReadOnlySet<string> Trends =
        new HashSet<string>(StringComparer.Ordinal) { "worsening", "stable" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Reads and validates the story list. Throws
    /// <see cref="InvalidDataException"/> listing every problem if the file is
    /// missing, unparseable, empty, or any story violates the domain taxonomy.</summary>
    public static List<StoryConfig> Load(string storiesPath, DomainDescriptor domain)
    {
        if (!File.Exists(storiesPath))
            throw new InvalidDataException(
                $"Story definitions not found for domain '{domain.Name}': {storiesPath}");

        List<StoryConfig>? stories;
        try
        {
            stories = JsonSerializer.Deserialize<List<StoryConfig>>(File.ReadAllText(storiesPath), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{storiesPath}: not valid JSON — {ex.Message}");
        }

        if (stories is null || stories.Count == 0)
            throw new InvalidDataException($"{storiesPath}: must define at least one planted story.");

        var failures = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var story in stories)
        {
            var label = $"stories['{story.Id}']";
            if (string.IsNullOrWhiteSpace(story.Id) || !ids.Add(story.Id))
                failures.Add($"{label}: id must be set and unique.");
            if (!domain.Categories.Contains(story.Category))
                failures.Add($"{label}: category '{story.Category}' is not a '{domain.Name}' domain category.");
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

        if (failures.Count > 0)
            throw new InvalidDataException(
                $"{storiesPath}: invalid story definitions:\n  - " + string.Join("\n  - ", failures));

        return stories;
    }
}
