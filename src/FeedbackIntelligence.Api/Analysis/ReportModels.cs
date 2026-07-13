using FeedbackIntelligence.Core.Alerts;

namespace FeedbackIntelligence.Api.Analysis;

/// <summary>An alert row in the management view. Deterministic hits and
/// LLM nominations are visibly distinct; both link to their source item.</summary>
public sealed record ReportAlert(
    string FeedbackId,
    string Source,
    string Timestamp,
    string TextExcerpt,
    string Text,
    IReadOnlyList<AlertHit> DeterministicHits,
    string? LlmReason);

/// <summary>A source feedback message behind a theme, EMBEDDED in the report so
/// the view can show the evidence in one click — live or from a snapshot, with no
/// per-item fetch (works when the backend is down). Text is the full message
/// (already length-capped at ingest).</summary>
public sealed record ReportSourceItem(
    string FeedbackId,
    string Source,
    string Timestamp,
    string Text,
    string Severity,
    // Injection hardening (ADR-0021 A2): this item showed injection symptoms and was
    // flagged needs_review at ingest. It is NOT excluded from the group/trend —
    // excluding it would be exploitable (append injection phrases to a real critical
    // to get it suppressed) — so instead the influence is made VISIBLE here.
    bool NeedsReview = false,
    // Deterministic alert-lexicon hits on this item (e.g. retail's "rasismi",
    // ADR-0027) — the views tag the row so recognition is visible per comment.
    IReadOnlyList<string>? AlertCategories = null);

/// <summary>One theme group. Count, direction and the feedback IDs are computed
/// deterministically; only Title/Narrative come from the synthesis model, and
/// they are dropped to a deterministic fallback if their citations fail.
/// <paramref name="Direction"/> is a language-neutral KEY
/// (stable/growing/declining/worsening) — the verify gate and JSON key off it;
/// <paramref name="DirectionLabel"/> is its display text in the domain's language.
/// <paramref name="Sources"/> carries the group's messages (severity-first) so the
/// view lists them directly instead of dumping clickable IDs.</summary>
public sealed record ReportTheme(
    string Category,
    string Title,
    string Narrative,
    int Count,
    string Direction,
    string DirectionLabel,
    IReadOnlyList<string> FeedbackIds,
    bool NarrativeFromLlm,
    IReadOnlyList<ReportSourceItem> Sources,
    // Injection hardening (ADR-0021 A2): how many of this group's items are flagged
    // needs_review, so the view can warn that a possibly-manipulated item is part of
    // the (still-counted) group — the influence is visible, not silent.
    int FlaggedCount = 0,
    // Live-summary mode (ADR-0026): true when this group is an EMERGENT TOPIC —
    // catch-all items grouped by the structuring model's free-text theme, with
    // Title carrying the topic. The view keys its rendering off this flag rather
    // than re-deriving the grouping rule client-side.
    bool IsEmergentTopic = false);

/// <summary>DroppedClaimCount counts ONLY citation-validation failures (the
/// model made a claim it could not ground). LlmFallbackCount counts groups
/// where the model was unavailable/over budget/unparseable — an infrastructure
/// condition, never presented as model misbehavior.</summary>
public sealed record ManagementReport(
    string WindowFrom,
    string WindowTo,
    string GeneratedAt,
    int TotalItems,
    int StructureFailedCount,
    IReadOnlyList<ReportAlert> Alerts,
    IReadOnlyList<ReportTheme> Themes,
    int DroppedClaimCount,
    int LlmFallbackCount,
    // The active domain's language ("fi"/"en") so the snapshot page renders in the
    // right language even when the backend (and /schema) is unreachable.
    string Language,
    // Injection hardening (ADR-0021 A3): narratives dropped to the deterministic
    // fallback because they turned directive (recommend/act/verdict) instead of
    // describing — distinct from DroppedClaimCount (ungrounded citations).
    int ActionDroppedCount = 0,
    // Live-summary mode only (the desk segment): ONE whole-window narrative
    // synthesized over all items, rendered above the per-category sections.
    // Null on the standard report path. Reuses the ReportTheme shape (title,
    // narrative, count, direction, flagged count); its Sources stay empty —
    // the items live in the category sections.
    ReportTheme? Overall = null);
