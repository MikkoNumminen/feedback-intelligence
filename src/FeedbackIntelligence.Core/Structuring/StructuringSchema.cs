namespace FeedbackIntelligence.Core.Structuring;

/// <summary>
/// The UNIVERSAL shape of a structured feedback record — the five field names,
/// domain-independent. The VALUES of the domain-shaped fields (category /
/// severity / type) are NOT here; they come from the active domain module
/// (see <see cref="FeedbackIntelligence.Core.Domain.IActiveDomain"/>). Only the
/// field-name set is a core constant.
/// </summary>
public static class StructuringSchema
{
    /// <summary>
    /// Exactly these fields — no more, no fewer. Deliberately no alert field:
    /// alert decisions belong to the deterministic layer and the analysis pass,
    /// never to the structuring model.
    /// </summary>
    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        "category",
        "theme",
        "severity",
        "type",
        "language",
    };

    /// <summary>The OPTIONAL sixth field (ADR-0031): a model-authored sentiment
    /// key. Separate from <see cref="Fields"/> so its ABSENCE is never a
    /// missing-field violation — the report falls back to the deterministic
    /// type→sentiment map (ADR-0030) when it is not present.</summary>
    public static readonly IReadOnlySet<string> OptionalFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "sentiment",
    };

    /// <summary>Every field name the schema recognizes (required + optional). A
    /// field outside this set is a genuine extra field.</summary>
    public static readonly IReadOnlySet<string> KnownFields =
        new HashSet<string>(Fields.Concat(OptionalFields), StringComparer.Ordinal);
}
