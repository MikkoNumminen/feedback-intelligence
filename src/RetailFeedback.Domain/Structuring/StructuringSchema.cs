namespace RetailFeedback.Domain.Structuring;

/// <summary>
/// Structuring schema v0, approved 2026-07-03 (see CLAUDE.md, "Phase 0 approved
/// decisions"). Single source of truth for the field names and enum values the
/// structuring model must produce; the eval runner and the ingest pipeline both
/// validate against these sets.
/// </summary>
public static class StructuringSchema
{
    public static readonly IReadOnlySet<string> Departments = new HashSet<string>(StringComparer.Ordinal)
    {
        "maito_kylma",
        "hevi",
        "kuiva_elintarvike",
        "liha_kala",
        "leipa",
        "kassa_palvelu",
        "piha_puutarha",
        "rakennustarvike",
        "tyokalut",
        "sisustus_maalit",
        "sahko_lvi",
        "varasto_nouto",
        "verkkokauppa_toimitus",
        "muu",
    };

    public static readonly IReadOnlySet<string> Severities = new HashSet<string>(StringComparer.Ordinal)
    {
        "low",
        "medium",
        "high",
        "critical",
    };

    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal)
    {
        "complaint",
        "praise",
        "suggestion",
        "question",
        "other",
    };

    /// <summary>
    /// Exactly these fields — no more, no fewer. Deliberately no alert field:
    /// alert decisions belong to the deterministic layer and the analysis pass,
    /// never to the structuring model.
    /// </summary>
    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        "department",
        "theme",
        "severity",
        "type",
        "language",
    };
}
