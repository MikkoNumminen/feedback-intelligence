using Microsoft.Extensions.Options;
using FeedbackIntelligence.Api.Alerts;
using FeedbackIntelligence.Api.Storage;
using FeedbackIntelligence.Core.Alerts;
using FeedbackIntelligence.Core.Security;
using FeedbackIntelligence.Core.Structuring;
using FeedbackIntelligence.Llm.Structuring;

namespace FeedbackIntelligence.Api.Ingest;

/// <summary>
/// The one ingest path all four sources share. Order is a design decision:
/// the deterministic alert layer runs FIRST and its result is stored no matter
/// what the LLM does; structuring failures never lose the feedback
/// (structure_failed + raw text preserved).
/// </summary>
public sealed class IngestService(
    FeedbackStore store,
    IStructuringService structuringService,
    LlmGate llmGate,
    AlertKeywordSet keywords,
    Analysis.ReportCache reportCache,
    ILogger<IngestService> logger)
{
    public async Task<StoredFeedback> IngestAsync(FeedbackRequest request, CancellationToken ct)
    {
        // Idempotency pre-check before any LLM work: a client retry with the
        // same id must not burn a GPU slot only to hit the PK constraint.
        if (!string.IsNullOrWhiteSpace(request.Id) && await store.GetAsync(request.Id!, ct) is not null)
            throw new DuplicateFeedbackIdException(request.Id!);

        if (!TimestampNormalizer.TryNormalize(request.Timestamp, out var normalizedTimestamp))
            throw new ArgumentException($"unparseable timestamp '{request.Timestamp}' reached ingest — validate first");

        var alerts = AlertMatcher.Match(request.Text, keywords.Categories);
        if (alerts.Count > 0)
            logger.LogInformation("Deterministic alerts on ingest: {Alerts}",
                string.Join(", ", alerts.Select(a => $"{a.Category}:{a.Pattern}")));

        FeedbackStructure? structure;
        var failed = false;
        IReadOnlyList<string> notes = [];

        if (request.AcceptedStructure is not null)
        {
            // Desk path: the human already accepted/corrected the interpretation —
            // store it as-is with the audit; no second LLM pass.
            structure = request.AcceptedStructure;
        }
        else
        {
            try
            {
                var result = await llmGate.RunAsync(
                    innerCt => structuringService.StructureAsync(request.Text, innerCt), ct);
                structure = result.Structure;
                failed = result.Failed;
                notes = result.Notes;
                if (result.Failed)
                    logger.LogWarning("structure_failed on ingest; raw feedback preserved. Notes: {Notes}",
                        string.Join("; ", result.Notes));
            }
            catch (LlmBusyException)
            {
                // Shed, don't store: the client gets a clean 503 and retries.
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // LLM down ≠ feedback lost: store unstructured, flag it, move on.
                logger.LogError(ex, "LLM unavailable during ingest; storing structure_failed.");
                structure = null;
                failed = true;
                notes = [$"llm call failed: {ex.Message}"];
            }
        }

        // Injection hardening (ADR-0021 A2): a deterministic scan of the raw text for
        // prompt-injection symptoms. It never drops or alters the item — it raises a
        // needs_review flag so a manipulated item cannot SILENTLY shape output, and
        // adds a higher-risk flag when a symptom co-occurs with a model-assigned severe
        // rating (the "talked-into-critical" case). The A1 fence governs the
        // structuring call; this catches the residual A1 cannot: an in-band imperative
        // that stays inside the data block.
        //
        // Skipped on the desk AcceptedStructure path: a human already saw the
        // interpretation at /interpret and accepted/corrected it, so needs_review ("a
        // human should validate") is already met — and the co-occurrence flag's
        // "model-assigned severe rating" meaning doesn't hold for a human-chosen one.
        var reviewFlags = new List<string>();
        if (request.AcceptedStructure is null)
        {
            reviewFlags.AddRange(InjectionSignals.Detect(request.Text));
            if (reviewFlags.Count > 0
                && structure is not null
                && InjectionSignals.SevereSeverities.Contains(structure.Severity))
                reviewFlags.Add(InjectionSignals.SevereRatingFlag);
        }
        if (reviewFlags.Count > 0)
            logger.LogWarning("Feedback flagged needs_review (injection symptoms): {Flags}",
                string.Join(", ", reviewFlags));

        var stored = new StoredFeedback(
            Id: string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id!,
            Source: request.Source,
            Text: request.Text,
            Timestamp: normalizedTimestamp,
            CreatedAt: DateTimeOffset.UtcNow.ToString("O"),
            Structure: structure,
            StructureFailed: failed,
            ModelFailed: request.ModelInterpretationFailed == true,
            SalvageNotes: notes,
            Alerts: alerts,
            Corrections: request.Corrections,
            NeedsReview: reviewFlags.Count > 0,
            ReviewFlags: reviewFlags);

        await store.InsertAsync(stored, ct);
        // The live-view centerpiece: a fresh desk entry must appear on the very
        // next report refresh, so the report cache cannot outlive an ingest.
        reportCache.Invalidate();
        return stored;
    }

    /// <summary>Operator maintenance (ADR-0026): re-run the structuring model over
    /// every stored item with the CURRENT domain vocabulary, so a category added
    /// later (retail's "asiaton") re-adapts entries stored under the old one.
    /// Every result is model-assigned: the A2 injection-symptom scan applies to
    /// each item, and stale human corrections are cleared by the store update so
    /// the correction telemetry never counts audits of a structure that no longer
    /// exists. Sheds LlmBusy to the caller (the operator retries); a per-item LLM
    /// failure stores structure_failed and moves on — feedback is never lost.</summary>
    public async Task<(int Restructured, int Failed, int Total)> RestructureAllAsync(CancellationToken ct)
    {
        // Newest-first cap of 500: far above any live-channel population; the
        // main channel's corpus would re-run the generator instead.
        var items = await store.QueryAsync(null, null, 500, ct);
        var restructured = 0;
        var failed = 0;
        foreach (var item in items)
        {
            FeedbackStructure? structure;
            var itemFailed = false;
            IReadOnlyList<string> notes = [];
            try
            {
                var result = await llmGate.RunAsync(
                    innerCt => structuringService.StructureAsync(item.Text, innerCt), ct);
                structure = result.Structure;
                itemFailed = result.Failed;
                notes = result.Notes;
            }
            catch (LlmBusyException)
            {
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LLM unavailable during restructure of {Id}; storing structure_failed.", item.Id);
                structure = null;
                itemFailed = true;
                notes = [$"llm call failed: {ex.Message}"];
            }

            var reviewFlags = new List<string>(InjectionSignals.Detect(item.Text));
            if (reviewFlags.Count > 0
                && structure is not null
                && InjectionSignals.SevereSeverities.Contains(structure.Severity))
                reviewFlags.Add(InjectionSignals.SevereRatingFlag);

            await store.UpdateStructureAsync(item.Id, structure, itemFailed, notes, reviewFlags.Count > 0, reviewFlags, ct);
            if (itemFailed) failed++; else restructured++;
        }
        reportCache.Invalidate();
        logger.LogInformation("Restructure pass: {Ok} restructured, {Failed} failed, {Total} total.",
            restructured, failed, items.Count);
        return (restructured, failed, items.Count);
    }
}
