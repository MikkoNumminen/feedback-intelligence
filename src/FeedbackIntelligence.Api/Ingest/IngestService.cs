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
    Core.Domain.IActiveDomain activeDomain,
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
            (structure, failed, notes) = await StructureViaGateAsync(request.Text, "ingest", ct);
        }

        // ADR-0027: a category-alert (lexicon category that IS a declared
        // structuring category, e.g. retail's "rasismi") categorizes the item
        // deterministically. It outranks the model AND the desk-accepted
        // structure: /interpret already previews the forced category, so a
        // mismatch here means it was edited away — the rule re-asserts it.
        var overrideCategory = AlertMatcher.CategoryOverride(alerts, activeDomain.Descriptor.Categories);
        var corrections = request.Corrections;
        var forcedStructure = AlertMatcher.ApplyCategoryOverride(structure, overrideCategory);
        if (!ReferenceEquals(forcedStructure, structure))
        {
            logger.LogInformation("Category-alert override: {Old} -> {New}", structure!.Category, overrideCategory);
            // A desk clerk's category correction that the rule just discarded
            // must not be stored as an audit: telemetry would count a human
            // category-choice whose value isn't in the stored structure — the
            // same staleness RestructureAsync clears for (other fields' audits
            // still describe the stored structure and are kept).
            if (request.AcceptedStructure is not null)
                corrections = corrections?.Where(c => c.Field != "category").ToList();
            structure = forcedStructure;
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
        var reviewFlags = request.AcceptedStructure is null
            ? BuildReviewFlags(request.Text, structure)
            : [];
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
            Corrections: corrections,
            NeedsReview: reviewFlags.Count > 0,
            ReviewFlags: reviewFlags);

        await store.InsertAsync(stored, ct);
        // The live-view centerpiece: a fresh desk entry must appear on the very
        // next report refresh, so the report cache cannot outlive an ingest.
        reportCache.Invalidate();
        return stored;
    }

    /// <summary>Operator maintenance (ADR-0026): re-run the structuring model over
    /// stored items whose structure NEEDS the current vocabulary — unstructured
    /// items, catch-all items (a new category or emergent topic may now fit), and
    /// items whose category no longer exists in the domain. Items sitting in a
    /// still-valid named category are SKIPPED: a human desk-acceptance there is a
    /// deliberate audit this pass must not overwrite. Re-structured results are
    /// model-assigned: the A2 injection-symptom scan applies, and stale human
    /// corrections are cleared by the store update so the correction telemetry
    /// never counts audits of a structure that no longer exists. Sheds LlmBusy to
    /// the caller (the operator retries); a per-item LLM failure stores
    /// structure_failed and moves on — feedback is never lost. The cache is
    /// invalidated even when the pass aborts mid-way, so a partial pass can never
    /// leave a stale report over half-updated rows.</summary>
    public async Task<(int Restructured, int Failed, int Skipped, int AlertsUpdated, int Total)> RestructureAsync(
        Core.Domain.DomainDescriptor domain, CancellationToken ct)
    {
        // Newest-first cap of 500: far above any live-channel population; the
        // main channel's corpus would re-run the generator instead.
        var items = await store.QueryAsync(null, null, 500, ct);
        var restructured = 0;
        var failed = 0;
        var alertsUpdated = 0;
        var updatedAny = false;
        try
        {
            foreach (var item in items)
            {
                // Alerts re-stamp for EVERY item, not just the bounded structure
                // scope: the lexicon is deterministic rule data no human edits, so
                // a new category (retail's "rasismi", ADR-0027) must recognize
                // already-stored comments too — cheap, no LLM.
                var currentAlerts = AlertMatcher.Match(item.Text, keywords.Categories);
                // AlertHit is a record — SequenceEqual is value equality over
                // every field, so a new field can never silently escape the diff.
                var alertsChanged = !currentAlerts.SequenceEqual(item.Alerts);
                if (alertsChanged && await store.UpdateAlertsAsync(item.Id, currentAlerts, ct) > 0)
                {
                    alertsUpdated++;
                    updatedAny = true;
                }

                var overrideCategory = AlertMatcher.CategoryOverride(currentAlerts, domain.Categories);
                var needsPass = item.Structure is null
                    || item.Structure.Category == domain.CatchAllCategory
                    || !domain.Categories.Contains(item.Structure.Category);
                if (!needsPass)
                {
                    // A category-alert (ADR-0027) re-categorizes even an item the
                    // bounded scope would skip — deterministic, no LLM, and it
                    // outranks the human audit for the same reason it outranks
                    // desk acceptance at ingest. Everything but the category
                    // (incl. review flags) is carried over unchanged; the store
                    // update clears corrections like any restructure, since they
                    // audited a categorization that no longer stands.
                    var forced = AlertMatcher.ApplyCategoryOverride(item.Structure, overrideCategory);
                    if (!ReferenceEquals(forced, item.Structure))
                    {
                        var forcedRows = await store.UpdateStructureAsync(
                            item.Id, forced, item.StructureFailed, item.SalvageNotes,
                            item.NeedsReview, item.ReviewFlags ?? [], ct);
                        if (forcedRows > 0)
                        {
                            restructured++;
                            updatedAny = true;
                        }
                    }
                    continue;
                }

                var (structure, itemFailed, notes) = await StructureViaGateAsync(item.Text, $"restructure of {item.Id}", ct);
                structure = AlertMatcher.ApplyCategoryOverride(structure, overrideCategory);
                var reviewFlags = BuildReviewFlags(item.Text, structure);
                var updatedRows = await store.UpdateStructureAsync(
                    item.Id, structure, itemFailed, notes, reviewFlags.Count > 0, reviewFlags, ct);
                if (updatedRows == 0)
                    continue; // row vanished between the snapshot and the update — not a success
                updatedAny = true;
                if (itemFailed) failed++; else restructured++;
            }
        }
        finally
        {
            // A partial pass (LlmBusy shed, cancellation) already changed rows —
            // the cached report must not outlive them.
            if (updatedAny)
                reportCache.Invalidate();
        }
        var skipped = items.Count - restructured - failed;
        logger.LogInformation(
            "Restructure pass: {Ok} restructured, {Failed} failed, {Skipped} skipped, {Alerts} alert re-stamps, {Total} total.",
            restructured, failed, skipped, alertsUpdated, items.Count);
        return (restructured, failed, skipped, alertsUpdated, items.Count);
    }

    /// <summary>The gated structuring call with the ingest error contract, shared
    /// by ingest and restructure so their failure semantics cannot drift:
    /// LlmBusy sheds to the caller, cancellation propagates, anything else
    /// degrades to structure_failed with the raw text preserved.</summary>
    private async Task<(FeedbackStructure? Structure, bool Failed, IReadOnlyList<string> Notes)> StructureViaGateAsync(
        string text, string context, CancellationToken ct)
    {
        try
        {
            var result = await llmGate.RunAsync(innerCt => structuringService.StructureAsync(text, innerCt), ct);
            if (result.Failed)
                logger.LogWarning("structure_failed on {Context}; raw feedback preserved. Notes: {Notes}",
                    context, string.Join("; ", result.Notes));
            return (result.Structure, result.Failed, result.Notes);
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
            logger.LogError(ex, "LLM unavailable during {Context}; storing structure_failed.", context);
            return (null, true, [$"llm call failed: {ex.Message}"]);
        }
    }

    /// <summary>The ADR-0021 A2 injection-symptom scan for MODEL-ASSIGNED
    /// structures, shared by ingest and restructure so a security fix to the
    /// scan can never apply to one path and not the other.</summary>
    private static List<string> BuildReviewFlags(string text, FeedbackStructure? structure)
    {
        var reviewFlags = new List<string>(InjectionSignals.Detect(text));
        if (reviewFlags.Count > 0
            && structure is not null
            && InjectionSignals.SevereSeverities.Contains(structure.Severity))
            reviewFlags.Add(InjectionSignals.SevereRatingFlag);
        return reviewFlags;
    }
}
