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

/// <summary>One theme group. Count, direction and the feedback IDs are computed
/// deterministically; only Title/Narrative come from the synthesis model, and
/// they are dropped to a deterministic fallback if their citations fail.</summary>
public sealed record ReportTheme(
    string Category,
    string Title,
    string Narrative,
    int Count,
    string Direction,
    IReadOnlyList<string> FeedbackIds,
    bool NarrativeFromLlm);

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
    int LlmFallbackCount);
