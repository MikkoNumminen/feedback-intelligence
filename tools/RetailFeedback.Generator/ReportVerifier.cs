using System.Text.Json;

namespace RetailFeedback.Generator;

/// <summary>
/// The Phase 4 acceptance mechanism, mechanized: verifies a management-report
/// JSON against a ground-truth file by ID matching — "the report's claim
/// grounds to >= minGroundedIds of these specific IDs within this window",
/// never "the report mentions dairy". Pure and dependency-free: the report is
/// read structurally (JsonDocument), so this tool never references the API.
/// </summary>
public static class ReportVerifier
{
    public sealed record StoryResult(
        string StoryId,
        bool GroundingPass,
        int GroundedIds,
        int RequiredIds,
        bool TrendPass,
        string ExpectedTrend,
        string ReportedDirection,
        bool AlertPass,
        bool AlertExpected,
        bool KeywordSeen)
    {
        public bool Pass => GroundingPass && TrendPass && AlertPass;
    }

    /// <summary>Trend acceptance: "worsening" is satisfied by volume growth with
    /// or without the severity shift; "stable" only by "vakaa".</summary>
    private static readonly Dictionary<string, string[]> AcceptedDirections = new(StringComparer.Ordinal)
    {
        ["worsening"] = ["paheneva", "kasvava"],
        ["stable"] = ["vakaa"],
    };

    public static List<StoryResult> Verify(string groundTruthJson, string reportJson)
    {
        using var truth = JsonDocument.Parse(groundTruthJson);
        using var report = JsonDocument.Parse(reportJson);

        var themes = report.RootElement.TryGetProperty("themes", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().ToList()
            : [];
        var alertIds = report.RootElement.TryGetProperty("alerts", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray()
                .Select(x => x.TryGetProperty("feedbackId", out var id) ? id.GetString() : null)
                .Where(id => id is not null)
                .ToHashSet(StringComparer.Ordinal)!
            : new HashSet<string?>();

        var results = new List<StoryResult>();
        foreach (var story in truth.RootElement.GetProperty("stories").EnumerateArray())
        {
            var storyId = story.GetProperty("id").GetString()!;
            var department = story.GetProperty("expectedDepartment").GetString()!;
            var expectedIds = story.GetProperty("feedbackIds").EnumerateArray()
                .Select(e => e.GetString()!)
                .ToHashSet(StringComparer.Ordinal);
            var minGrounded = story.GetProperty("minGroundedIds").GetInt32();
            var expectedTrend = story.GetProperty("trend").GetString()!;
            var expectAlert = story.GetProperty("expectAlert").GetBoolean();
            var keywords = story.GetProperty("expectedThemeKeywords").EnumerateArray()
                .Select(e => e.GetString()!)
                .ToList();

            // The story must ground inside the theme(s) of its expected
            // department — grounding elsewhere is a misclassification.
            var departmentThemes = themes
                .Where(theme => theme.GetProperty("department").GetString() == department)
                .ToList();
            var themeIds = departmentThemes
                .SelectMany(theme => theme.GetProperty("feedbackIds").EnumerateArray())
                .Select(e => e.GetString()!)
                .ToHashSet(StringComparer.Ordinal);
            var grounded = expectedIds.Count(themeIds.Contains);

            var direction = departmentThemes.Count > 0
                ? departmentThemes[0].GetProperty("direction").GetString()!
                : "(ei teemaa)";
            var trendPass = AcceptedDirections.TryGetValue(expectedTrend, out var accepted)
                && accepted.Contains(direction, StringComparer.Ordinal);

            var alertPass = !expectAlert || expectedIds.Any(id => alertIds.Contains(id));

            // Informational only — narratives may phrase things differently;
            // the ID grounding above is the gate.
            var narrativeText = string.Join(" ", departmentThemes.Select(theme =>
                $"{theme.GetProperty("title").GetString()} {theme.GetProperty("narrative").GetString()}"));
            var keywordSeen = keywords.Any(k => narrativeText.Contains(k, StringComparison.OrdinalIgnoreCase));

            results.Add(new StoryResult(
                storyId,
                grounded >= minGrounded,
                grounded,
                minGrounded,
                trendPass,
                expectedTrend,
                direction,
                alertPass,
                expectAlert,
                keywordSeen));
        }
        return results;
    }
}
