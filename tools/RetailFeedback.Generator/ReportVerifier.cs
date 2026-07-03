using System.Globalization;
using System.Text.Json;

namespace RetailFeedback.Generator;

/// <summary>
/// The Phase 4 acceptance mechanism, mechanized: verifies a management-report
/// JSON against a ground-truth file by ID matching — "the report's claim
/// grounds to >= minGroundedIds of these specific IDs within this window",
/// never "the report mentions dairy". Pure and dependency-free: the report is
/// read structurally (JsonDocument), so this tool never references the API.
///
/// Gate design (review 2026-07-04): grounding, alert and window coverage are
/// HARD gates — they are story-owned and deterministic. Trend is a WARNING
/// tier: the report's direction is a department AGGREGATE, and same-department
/// noise (untagged corpus items the LLM classifies into a story's department)
/// legitimately dilutes it. A diluted trend does not fail acceptance, but it
/// is surfaced loudly — it means the planted story is less visible in the
/// demo, which the corpus author wants to know.
/// </summary>
public static class ReportVerifier
{
    public sealed record StoryResult(
        string StoryId,
        bool WindowCovered,
        bool GroundingPass,
        int GroundedIds,
        int RequiredIds,
        bool TrendOk,
        string ExpectedTrend,
        string ReportedDirection,
        bool AlertPass,
        bool AlertExpected,
        bool KeywordSeen)
    {
        public bool Pass => WindowCovered && GroundingPass && AlertPass;
    }

    /// <summary>"worsening" is satisfied by volume growth with or without the
    /// severity shift; "stable" by "vakaa". Anything else is a dilution warning.</summary>
    private static readonly Dictionary<string, string[]> AcceptedDirections = new(StringComparer.Ordinal)
    {
        ["worsening"] = ["paheneva", "kasvava"],
        ["stable"] = ["vakaa"],
    };

    public static List<StoryResult> Verify(string groundTruthJson, string reportJson)
    {
        using var truth = JsonDocument.Parse(groundTruthJson);
        using var report = JsonDocument.Parse(reportJson);

        var stories = truth.RootElement.GetProperty("stories").EnumerateArray().ToList();
        if (stories.Count == 0)
            throw new InvalidDataException("Ground truth contains no stories — nothing to verify (wrong file?).");

        var themes = report.RootElement.TryGetProperty("themes", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().ToList()
            : [];
        var alertIds = report.RootElement.TryGetProperty("alerts", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray()
                .Select(x => x.TryGetProperty("feedbackId", out var id) ? id.GetString() : null)
                .Where(id => id is not null)
                .ToHashSet(StringComparer.Ordinal)!
            : new HashSet<string?>();
        var (reportFrom, reportTo) = ReadReportWindow(report.RootElement);

        var results = new List<StoryResult>();
        foreach (var story in stories)
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

            // Operator-error detector: a report generated over the wrong window
            // produces "grounding 0/N" for every story — indistinguishable from
            // a real regression unless the window mismatch is named.
            var windowCovered = IsWindowCovered(story, reportFrom, reportTo);

            // The story must ground inside the theme(s) of its expected
            // department — grounding elsewhere is a misclassification. Extra
            // (noise) IDs in the theme are expected and do not matter.
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
            var trendOk = AcceptedDirections.TryGetValue(expectedTrend, out var accepted)
                && accepted.Contains(direction, StringComparer.Ordinal);

            var alertPass = !expectAlert || expectedIds.Any(id => alertIds.Contains(id));

            // Informational only — narratives may phrase things differently;
            // the ID grounding above is the gate.
            var narrativeText = string.Join(" ", departmentThemes.Select(theme =>
                $"{theme.GetProperty("title").GetString()} {theme.GetProperty("narrative").GetString()}"));
            var keywordSeen = keywords.Any(k => narrativeText.Contains(k, StringComparison.OrdinalIgnoreCase));

            results.Add(new StoryResult(
                storyId,
                windowCovered,
                grounded >= minGrounded,
                grounded,
                minGrounded,
                trendOk,
                expectedTrend,
                direction,
                alertPass,
                expectAlert,
                keywordSeen));
        }
        return results;
    }

    private static (DateOnly? From, DateOnly? To) ReadReportWindow(JsonElement report)
    {
        DateOnly? Parse(string property) =>
            report.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? DateOnly.FromDateTime(parsed.UtcDateTime)
                : null;
        return (Parse("windowFrom"), Parse("windowTo"));
    }

    private static bool IsWindowCovered(JsonElement story, DateOnly? reportFrom, DateOnly? reportTo)
    {
        // Tolerant by design: if either side is unparseable, coverage cannot be
        // judged and must not fail the gate.
        if (reportFrom is null || reportTo is null)
            return true;
        if (!DateOnly.TryParseExact(story.GetProperty("windowFrom").GetString(), "yyyy-MM-dd", out var storyFrom)
            || !DateOnly.TryParseExact(story.GetProperty("windowTo").GetString(), "yyyy-MM-dd", out var storyTo))
            return true;
        return reportFrom <= storyFrom && reportTo >= storyTo;
    }
}
