using System.Globalization;
using Microsoft.Extensions.Options;
using RetailFeedback.Api.Storage;
using RetailFeedback.Domain.Structuring;

namespace RetailFeedback.Api.Telemetry;

/// <summary>
/// The ongoing quality measure that replaced the cancelled up-front model eval
/// (CLAUDE.md, Phase 0 closure): summarizes per-field correction rates from
/// the desk audit trail over time. Rising rates = the structuring model is
/// drifting or underperforming on real input; the model stays swappable by
/// config if this data ever says so.
/// </summary>
public sealed record FieldCorrectionRate(string Field, int Corrections, double Rate);

public sealed record WeeklyCorrections(string WeekStart, int DeskEntries, int Corrections);

public sealed record CorrectionTelemetry(
    string WindowFrom,
    string WindowTo,
    int DeskEntries,
    int ModelInterpreted,
    int ModelFailed,
    IReadOnlyList<FieldCorrectionRate> PerField,
    IReadOnlyList<WeeklyCorrections> Weekly);

public sealed class CorrectionTelemetryService(FeedbackStore store, IOptions<IngestOptions> options)
{
    public async Task<CorrectionTelemetry> SummarizeAsync(string fromIso, string toIso, CancellationToken ct)
    {
        var deskItems = await store.QueryAsync(fromIso, toIso, options.Value.QueryMaxLimit, ct, source: "desk");

        // Rate denominator: entries where the model actually made an
        // interpretation. Manual entries after a failed interpretation are
        // counted separately — they are model failures, not zero-correction
        // successes.
        var modelFailed = deskItems.Count(i => i.ModelFailed);
        var interpreted = deskItems.Count - modelFailed;

        var perField = StructuringSchema.Fields
            .Select(field =>
            {
                var corrections = deskItems.Count(i =>
                    i.Corrections?.Any(c => c.Field == field) == true);
                return new FieldCorrectionRate(
                    field,
                    corrections,
                    interpreted > 0 ? Math.Round((double)corrections / interpreted, 3) : 0);
            })
            .OrderByDescending(f => f.Corrections)
            .ThenBy(f => f.Field, StringComparer.Ordinal)
            .ToList();

        var weekly = deskItems
            .Select(i => (Item: i, Week: IsoWeekStart(i.Timestamp)))
            .Where(x => x.Week is not null)
            .GroupBy(x => x.Week!)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new WeeklyCorrections(
                g.Key,
                g.Count(),
                g.Count(x => x.Item.Corrections is { Count: > 0 })))
            .ToList();

        return new CorrectionTelemetry(
            fromIso, toIso, deskItems.Count, interpreted, modelFailed, perField, weekly);
    }

    private static string? IsoWeekStart(string timestamp)
    {
        if (!DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
            return null;
        var date = DateOnly.FromDateTime(ts.UtcDateTime);
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday-start weeks
        return date.AddDays(-offset).ToString("yyyy-MM-dd");
    }
}
