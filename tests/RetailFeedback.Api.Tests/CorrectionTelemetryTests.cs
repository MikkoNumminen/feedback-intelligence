using Microsoft.Extensions.Options;
using RetailFeedback.Api;
using RetailFeedback.Api.Storage;
using RetailFeedback.Api.Telemetry;
using RetailFeedback.Domain.Structuring;

namespace RetailFeedback.Api.Tests;

public class CorrectionTelemetryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"telemetry-test-{Guid.NewGuid():N}.db");
    private readonly FeedbackStore _store;
    private readonly CorrectionTelemetryService _service;

    public CorrectionTelemetryTests()
    {
        var options = Options.Create(new IngestOptions { DbPath = _dbPath });
        _store = new FeedbackStore(options);
        _store.Initialize();
        _service = new CorrectionTelemetryService(_store, options);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private static StoredFeedback Desk(
        string id, string timestamp, List<FieldCorrection>? corrections = null, bool modelFailed = false) => new(
        id, "desk", "teksti", timestamp, timestamp,
        new FeedbackStructure("maito_kylma", "tuoreus", "high", "complaint", "fi"),
        false, modelFailed, [], [], corrections);

    [Fact]
    public async Task PerFieldRates_UseInterpretedEntriesAsDenominator()
    {
        // Week 1: two interpreted entries, one severity correction.
        await _store.InsertAsync(Desk("d1", "2026-06-22T10:00:00.0000000+00:00",
            [new FieldCorrection("severity", "medium", "high")]), CancellationToken.None);
        await _store.InsertAsync(Desk("d2", "2026-06-23T10:00:00.0000000+00:00"), CancellationToken.None);
        // Week 2: a manual entry after a failed interpretation — excluded from
        // the rate denominator, counted as a model failure.
        await _store.InsertAsync(Desk("d3", "2026-06-29T10:00:00.0000000+00:00", modelFailed: true), CancellationToken.None);
        // Non-desk item in the window must not count at all.
        await _store.InsertAsync(Desk("e1", "2026-06-23T11:00:00.0000000+00:00") with { Source = "email" }, CancellationToken.None);

        var telemetry = await _service.SummarizeAsync(
            "2026-06-18T00:00:00.0000000+00:00", "2026-07-01T00:00:00.0000000+00:00", CancellationToken.None);

        Assert.Equal(3, telemetry.DeskEntries);
        Assert.Equal(2, telemetry.ModelInterpreted);
        Assert.Equal(1, telemetry.ModelFailed);
        var severity = telemetry.PerField.Single(f => f.Field == "severity");
        Assert.Equal(1, severity.Corrections);
        Assert.Equal(0.5, severity.Rate);
        Assert.Equal(0, telemetry.PerField.Single(f => f.Field == "department").Corrections);
    }

    [Fact]
    public async Task WeeklyBuckets_AreMondayStart_AndOrdered()
    {
        await _store.InsertAsync(Desk("d1", "2026-06-22T10:00:00.0000000+00:00",
            [new FieldCorrection("theme", "a", "b")]), CancellationToken.None); // Mon 22.6
        await _store.InsertAsync(Desk("d2", "2026-06-28T10:00:00.0000000+00:00"), CancellationToken.None); // Sun same week
        await _store.InsertAsync(Desk("d3", "2026-06-29T10:00:00.0000000+00:00"), CancellationToken.None); // Mon next week

        var telemetry = await _service.SummarizeAsync(
            "2026-06-18T00:00:00.0000000+00:00", "2026-07-01T00:00:00.0000000+00:00", CancellationToken.None);

        Assert.Equal(2, telemetry.Weekly.Count);
        Assert.Equal("2026-06-22", telemetry.Weekly[0].WeekStart);
        Assert.Equal(2, telemetry.Weekly[0].DeskEntries);
        Assert.Equal(1, telemetry.Weekly[0].Corrections);
        Assert.Equal("2026-06-29", telemetry.Weekly[1].WeekStart);
    }

    [Fact]
    public async Task EmptyWindow_ProducesZeroesNotErrors()
    {
        var telemetry = await _service.SummarizeAsync(
            "2026-01-01T00:00:00.0000000+00:00", "2026-01-31T00:00:00.0000000+00:00", CancellationToken.None);

        Assert.Equal(0, telemetry.DeskEntries);
        Assert.All(telemetry.PerField, f => Assert.Equal(0, f.Rate));
        Assert.Empty(telemetry.Weekly);
    }
}
