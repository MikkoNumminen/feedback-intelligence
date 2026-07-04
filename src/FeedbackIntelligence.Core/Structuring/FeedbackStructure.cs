using System.Text.Json.Serialization;

namespace FeedbackIntelligence.Core.Structuring;

/// <summary>A schema-adherent structuring result (field semantics: schema v0 in CLAUDE.md).</summary>
public sealed record FeedbackStructure(
    [property: JsonPropertyName("department")] string Department,
    [property: JsonPropertyName("theme")] string Theme,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("language")] string Language);
