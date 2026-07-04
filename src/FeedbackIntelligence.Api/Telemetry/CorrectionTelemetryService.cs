using System.Globalization;
using Microsoft.Extensions.Options;
using FeedbackIntelligence.Api.Storage;
using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Api.Telemetry;

/// <summary>
/// The ongoing quality measure that replaced the cancelled up-front model eval
/// (ADR-0003): summarizes per-field correction rates from
/// the desk audit trail over time. Rising rates = the structuring model is
/// drifting or underperforming on real input; the model stays swappable by
/// config if this data ever says so.
///
/// Population discipline (review 2026-07-04): every rate uses ONE population —
/// model-interpreted desk entries. Manual entries after a failed
/// interpretation are counted separately (modelFailed) and contribute to no
/// numerator; weekly buckets expose the same split so weekly rates and the
/// headline rates are computed over the same definition.
/// </summary>
public sealed record FieldCorrectionRate(string Field, int Corrections, double Rate);

public sealed record WeeklyCorrections(
    string WeekStart,
    int DeskEntries,
    int Interpreted,
    int CorrectedEntries,
    IReadOnlyDictionary<string, int> PerField);

public sealed record CorrectionTelemetry(
    string WindowFrom,
    string WindowTo,
    int DeskEntries,
    int ModelInterpreted,
    int ModelFailed,
    bool Truncated,
    int UnbucketedEntries,
    IReadOnlyList<FieldCorrectionRate> PerField,
    IReadOnlyList<WeeklyCorrections> Weekly);

public sealed class CorrectionTelemetryService(FeedbackStore store, IOptions<IngestOptions> options)
{
    public async Task<CorrectionTelemetry> SummarizeAsync(string fromIso, string toIso, CancellationToken ct)
    {
        var deskItems = await store.QueryAsync(fromIso, toIso, options.Value.QueryMaxLimit, ct, source: "desk");
        // No silent caps: when the fetch cap bites, the response says so —
        // QueryAsync returns newest-first, so the OLDEST weeks would be the
        // ones silently missing.
        var truncated = deskItems.Count >= options.Value.QueryMaxLimit;

        var interpretedItems = deskItems.Where(i => !i.ModelFailed).ToList();
        var modelFailed = deskItems.Count - interpretedItems.Count;

        var perField = StructuringSchema.Fields
            .Select(field =>
            {
                var corrections = CountCorrected(interpretedItems, field);
                return new FieldCorrectionRate(
                    field,
                    corrections,
                    interpretedItems.Count > 0 ? Math.Round((double)corrections / interpretedItems.Count, 3) : 0);
            })
            .OrderByDescending(f => f.Corrections)
            .ThenBy(f => f.Field, StringComparer.Ordinal)
            .ToList();

        var bucketed = deskItems
            .Select(i => (Item: i, Week: IsoWeekStart(i.Timestamp)))
            .Where(x => x.Week is not null)
            .ToList();
        var weekly = bucketed
            .GroupBy(x => x.Week!)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var weekInterpreted = g.Where(x => !x.Item.ModelFailed).Select(x => x.Item).ToList();
                var weekPerField = StructuringSchema.Fields
                    .Select(field => (Field: field, Count: CountCorrected(weekInterpreted, field)))
                    .Where(x => x.Count > 0)
                    .ToDictionary(x => x.Field, x => x.Count, StringComparer.Ordinal);
                return new WeeklyCorrections(
                    g.Key,
                    g.Count(),
                    weekInterpreted.Count,
                    weekInterpreted.Count(i => i.Corrections is { Count: > 0 }),
                    weekPerField);
            })
            .ToList();

        return new CorrectionTelemetry(
            fromIso,
            toIso,
            deskItems.Count,
            interpretedItems.Count,
            modelFailed,
            truncated,
            deskItems.Count - bucketed.Count,
            perField,
            weekly);
    }

    /// <summary>Per-entry counting: two corrections on the same field in one
    /// entry count once — the rate reads "share of interpreted entries where a
    /// human corrected this field".</summary>
    private static int CountCorrected(IReadOnlyList<StoredFeedback> interpretedItems, string field) =>
        interpretedItems.Count(i => i.Corrections?.Any(c => c.Field == field) == true);

    private static string? IsoWeekStart(string timestamp)
    {
        if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
            return null;
        var date = DateOnly.FromDateTime(ts.UtcDateTime);
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday-start weeks
        return date.AddDays(-offset).ToString("yyyy-MM-dd");
    }
}
