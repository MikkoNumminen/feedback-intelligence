using System.Text.Json.Serialization;

namespace FeedbackIntelligence.Core.Structuring;

/// <summary>A schema-adherent structuring result (field semantics: schema v0 in docs/schema.md).</summary>
public sealed record FeedbackStructure(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("theme")] string Theme,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("language")] string Language);
