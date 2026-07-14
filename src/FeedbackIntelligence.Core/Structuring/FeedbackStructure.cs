using System.Text.Json.Serialization;

namespace FeedbackIntelligence.Core.Structuring;

/// <summary>A schema-adherent structuring result (field semantics: schema v0 in docs/schema.md).
/// <para><see cref="Sentiment"/> is the OPTIONAL sixth field (ADR-0031): a model-authored
/// polarity key, null when the model did not produce one (or an old stored row predates it) —
/// the report then falls back to the deterministic type→sentiment map (ADR-0030). Nullable +
/// last so every existing five-argument construction and every stored `structure_json` without
/// it stay valid; serialization omits it when null.</para></summary>
public sealed record FeedbackStructure(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("theme")] string Theme,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("sentiment")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Sentiment = null);
