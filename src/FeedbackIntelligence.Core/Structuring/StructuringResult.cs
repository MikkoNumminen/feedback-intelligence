namespace FeedbackIntelligence.Core.Structuring;

/// <summary>
/// Outcome of structuring one piece of feedback, including the salvage-layer
/// provenance: whether the JSON had to be salvaged from fences/prose, whether
/// safe normalization was applied, and whether a re-prompt was needed.
/// </summary>
public sealed record StructuringResult(
    FeedbackStructure? Structure,
    string RawResponse,
    bool Salvaged,
    bool Normalized,
    bool Retried,
    IReadOnlyList<string> Notes)
{
    /// <summary>
    /// structure_failed: no valid structure even after one retry. The raw
    /// feedback text (stored by the caller) and the raw model output here are
    /// both preserved — feedback is never lost, only its structuring.
    /// </summary>
    public bool Failed => Structure is null;
}
