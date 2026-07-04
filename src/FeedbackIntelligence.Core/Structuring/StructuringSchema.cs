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
}
