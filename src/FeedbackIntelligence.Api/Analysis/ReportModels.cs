using FeedbackIntelligence.Core.Alerts;

namespace FeedbackIntelligence.Api.Analysis;

/// <summary>An alert row in the management view. Deterministic hits and
/// LLM nominations are visibly distinct; both link to their source item.</summary>
public sealed record ReportAlert(
    string FeedbackId,
    string Source,
    string Timestamp,
    string TextExcerpt,
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
    string Severity);

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
    IReadOnlyList<ReportSourceItem> Sources);

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
    string Language);
