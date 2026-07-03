using RetailFeedback.Domain.Structuring;

namespace RetailFeedback.Api;

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
    List<FieldCorrection>? Corrections);

public sealed record InterpretRequest(string Text);

public sealed record FieldCorrection(string Field, string ModelValue, string HumanValue);

public sealed record FeedbackResponse(
    string Id,
    FeedbackStructure? Structure,
    bool StructureFailed,
    IReadOnlyList<string> SalvageNotes,
    IReadOnlyList<Domain.Alerts.AlertHit> Alerts);
