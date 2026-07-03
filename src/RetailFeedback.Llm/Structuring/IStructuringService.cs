using RetailFeedback.Domain.Structuring;

namespace RetailFeedback.Llm.Structuring;

/// <summary>
/// Structures one piece of raw feedback text via the configured structuring
/// model, through the mandatory salvage layer: parse → validate → normalize
/// where safe → re-prompt once → else a `structure_failed` result with the raw
/// output preserved. Never throws on model misbehavior; feedback is never lost.
/// </summary>
public interface IStructuringService
{
    Task<StructuringResult> StructureAsync(string feedbackText, CancellationToken ct = default);
}
