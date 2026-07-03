using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailFeedback.Api;
using RetailFeedback.Api.Alerts;
using RetailFeedback.Api.Ingest;
using RetailFeedback.Api.Storage;
using RetailFeedback.Domain.Structuring;
using RetailFeedback.Llm.Structuring;

namespace RetailFeedback.Api.Tests;

public class IngestServiceTests : IDisposable
{
    private static readonly FeedbackStructure ValidStructure =
        new("maito_kylma", "tuotteiden tuoreus", "high", "complaint", "fi");

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"feedback-test-{Guid.NewGuid():N}.db");
    private readonly IOptions<IngestOptions> _options;
    private readonly FeedbackStore _store;

    public IngestServiceTests()
    {
        _options = Options.Create(new IngestOptions { DbPath = _dbPath });
        _store = new FeedbackStore(_options);
        _store.Initialize();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private IngestService CreateService(FakeStructuring structuring) => new(
        _store,
        structuring,
        new LlmGate(_options),
        new AlertKeywordSet
        {
            Categories = new Dictionary<string, IReadOnlyList<string>> { ["injury_safety"] = ["loukkaantu"] },
        },
        new Analysis.ReportCache(),
        NullLogger<IngestService>.Instance);

    [Fact]
    public async Task AcceptedStructure_SkipsLlm_AndStoresAudit()
    {
        var structuring = new FakeStructuring(Success());
        var corrections = new List<FieldCorrection> { new("severity", "low", "high") };
        var request = new FeedbackRequest(
            "desk-001", "desk", "maito oli vanhaa", "2026-07-01T10:00:00+03:00", ValidStructure, corrections);

        var stored = await CreateService(structuring).IngestAsync(request, CancellationToken.None);

        Assert.Equal(0, structuring.Calls); // human-accepted → no second LLM pass
        Assert.Equal(ValidStructure, stored.Structure);
        var roundTrip = await _store.GetAsync("desk-001", CancellationToken.None);
        Assert.NotNull(roundTrip);
        Assert.Equal(corrections, roundTrip!.Corrections);
    }

    [Fact]
    public async Task StructuringPath_StoresStructureAndNotes()
    {
        var structuring = new FakeStructuring(Success(salvaged: true, notes: ["'department' was an array; kept first element"]));
        var request = new FeedbackRequest(null, "email", "maito oli vanhaa", "2026-07-01T10:00:00+03:00", null, null);

        var stored = await CreateService(structuring).IngestAsync(request, CancellationToken.None);

        Assert.Equal(1, structuring.Calls);
        Assert.Equal(ValidStructure, stored.Structure);
        Assert.Single(stored.SalvageNotes);
        Assert.False(string.IsNullOrEmpty(stored.Id)); // server-assigned id
    }

    [Fact]
    public async Task StructureFailed_NeverLosesFeedback()
    {
        var structuring = new FakeStructuring(new StructuringResult(
            null, "En osaa sanoa.", false, false, true, ["first attempt: no parseable JSON object"]));
        var request = new FeedbackRequest("f-001", "web_form", "sekava palaute", "2026-07-01T10:00:00+03:00", null, null);

        var stored = await CreateService(structuring).IngestAsync(request, CancellationToken.None);

        Assert.True(stored.StructureFailed);
        Assert.Null(stored.Structure);
        var roundTrip = await _store.GetAsync("f-001", CancellationToken.None);
        Assert.Equal("sekava palaute", roundTrip!.Text); // raw text preserved
    }

    [Fact]
    public async Task DeterministicAlerts_StoredEvenWhenStructuringFails()
    {
        var structuring = new FakeStructuring(new StructuringResult(null, "", false, false, true, []));
        var request = new FeedbackRequest(
            "f-002", "web_form", "Asiakas loukkaantui liukkaalla lattialla", "2026-07-01T10:00:00+03:00", null, null);

        var stored = await CreateService(structuring).IngestAsync(request, CancellationToken.None);

        // Alert layer runs FIRST and independent of the LLM — its result
        // survives any structuring outcome.
        Assert.True(stored.StructureFailed);
        Assert.Contains(stored.Alerts, a => a is { Category: "injury_safety", Pattern: "loukkaantu" });
    }

    [Fact]
    public async Task DuplicateId_ThrowsBeforeAnyLlmWork()
    {
        var structuring = new FakeStructuring(Success());
        var service = CreateService(structuring);
        var request = new FeedbackRequest("dup-001", "email", "eka palaute", "2026-07-01T10:00:00+03:00", null, null);
        await service.IngestAsync(request, CancellationToken.None);
        var callsAfterFirst = structuring.Calls;

        await Assert.ThrowsAsync<DuplicateFeedbackIdException>(() =>
            service.IngestAsync(request with { Text = "retry-palaute" }, CancellationToken.None));

        Assert.Equal(callsAfterFirst, structuring.Calls); // no GPU slot burnt on the retry
    }

    [Fact]
    public async Task Timestamps_AreStoredNormalizedUtc()
    {
        var structuring = new FakeStructuring(Success());
        var request = new FeedbackRequest("ts-001", "desk", "maito", "2026-07-01T10:00:00+03:00", ValidStructure, null);

        var stored = await CreateService(structuring).IngestAsync(request, CancellationToken.None);

        Assert.Equal("2026-07-01T07:00:00.0000000+00:00", stored.Timestamp);
    }

    [Fact]
    public async Task LlmConnectionFailure_StoresStructureFailed_NeverLosesFeedback()
    {
        var structuring = new ThrowingStructuring();
        var request = new FeedbackRequest("f-003", "email", "maito oli vanhaa", "2026-07-01T10:00:00+03:00", null, null);

        var stored = await CreateService2(structuring).IngestAsync(request, CancellationToken.None);

        Assert.True(stored.StructureFailed);
        Assert.Contains(stored.SalvageNotes, n => n.Contains("llm call failed"));
        var roundTrip = await _store.GetAsync("f-003", CancellationToken.None);
        Assert.Equal("maito oli vanhaa", roundTrip!.Text);
    }

    private IngestService CreateService2(IStructuringService structuring) => new(
        _store,
        structuring,
        new LlmGate(_options),
        new AlertKeywordSet
        {
            Categories = new Dictionary<string, IReadOnlyList<string>> { ["injury_safety"] = ["loukkaantu"] },
        },
        new Analysis.ReportCache(),
        NullLogger<IngestService>.Instance);

    private sealed class ThrowingStructuring : IStructuringService
    {
        public Task<StructuringResult> StructureAsync(string feedbackText, CancellationToken ct = default) =>
            throw new HttpRequestException("connection refused");
    }

    private static StructuringResult Success(bool salvaged = false, IReadOnlyList<string>? notes = null) =>
        new(ValidStructure, "{}", salvaged, false, false, notes ?? []);

    private sealed class FakeStructuring(StructuringResult result) : IStructuringService
    {
        public int Calls { get; private set; }

        public Task<StructuringResult> StructureAsync(string feedbackText, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
