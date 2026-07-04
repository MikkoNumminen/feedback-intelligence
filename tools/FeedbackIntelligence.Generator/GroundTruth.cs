using System.Text.Json.Serialization;

namespace FeedbackIntelligence.Generator;

/// <summary>
/// Machine-checkable ground truth for one planted story (Phase 1 decision,
/// 2026-07-03): exact feedback IDs, expected department enum value, expected
/// theme as a keyword set, time window, trend direction. Phase 4's acceptance
/// eval verifies a report claim by matching it against these IDs — "the
/// report's dairy claim grounds to >= minGroundedIds of these specific IDs
/// within this window", never "the report mentions dairy".
/// </summary>
public sealed record GroundTruthStory(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("feedbackIds")] IReadOnlyList<string> FeedbackIds,
    [property: JsonPropertyName("expectedDepartment")] string ExpectedDepartment,
    [property: JsonPropertyName("expectedThemeKeywords")] IReadOnlyList<string> ExpectedThemeKeywords,
    [property: JsonPropertyName("windowFrom")] string WindowFrom,
    [property: JsonPropertyName("windowTo")] string WindowTo,
    [property: JsonPropertyName("trend")] string Trend,
    [property: JsonPropertyName("minGroundedIds")] int MinGroundedIds,
    [property: JsonPropertyName("expectAlert")] bool ExpectAlert);

public sealed record GroundTruthFile(
    [property: JsonPropertyName("seed")] int Seed,
    [property: JsonPropertyName("anchorDate")] string AnchorDate,
    [property: JsonPropertyName("nonEvidential")] bool NonEvidential,
    [property: JsonPropertyName("stories")] IReadOnlyList<GroundTruthStory> Stories);
