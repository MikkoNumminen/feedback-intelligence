using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Api;

/// <summary>POST /feedback body. One endpoint, four sources — "channels" are
/// source values, not integrations. AcceptedStructure + Corrections are the
/// desk path (human-in-the-loop): when a structure is provided it is validated
/// and stored as-is with the correction audit; no LLM call happens.</summary>
public sealed record FeedbackRequest(
    string? Id,
    string Source,
    string Text,
    string Timestamp,
    FeedbackStructure? AcceptedStructure,
    List<FieldCorrection>? Corrections,
    // Desk manual path: the model's interpretation failed and a human authored
    // the structure from scratch. Without this marker those entries would look
    // like zero-correction model successes and the correction telemetry (the
    // drift detector replacing the cancelled eval) would undercount failures.
    bool? ModelInterpretationFailed = null);

public sealed record InterpretRequest(string Text);

public sealed record FieldCorrection(string Field, string ModelValue, string HumanValue);

public sealed record FeedbackResponse(
    string Id,
    FeedbackStructure? Structure,
    bool StructureFailed,
    IReadOnlyList<string> SalvageNotes,
    IReadOnlyList<FeedbackIntelligence.Core.Alerts.AlertHit> Alerts,
    // Injection hardening (ADR-0021 A2): surfaced so a caller/UI sees the item was
    // flagged for human review, and why (empty when clean).
    bool NeedsReview = false,
    IReadOnlyList<string>? ReviewFlags = null);
