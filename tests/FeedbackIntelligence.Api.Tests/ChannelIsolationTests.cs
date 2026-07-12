using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FeedbackIntelligence.Api;
using FeedbackIntelligence.Api.Alerts;
using FeedbackIntelligence.Api.Analysis;
using FeedbackIntelligence.Api.Ingest;
using FeedbackIntelligence.Api.Storage;
using FeedbackIntelligence.Core.Structuring;
using FeedbackIntelligence.Llm.Structuring;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>ADR-0024: the live desk channel gets its own database and its own
/// ReportCache, so a desk-live ingest can never leak into the seeded corpus and
/// invalidating the live report can never stampede the main report cache.</summary>
public class ChannelIsolationTests : IDisposable
{
    private static readonly FeedbackStructure ValidStructure =
        new("maito_kylma", "tuotteiden tuoreus", "high", "complaint", "fi");

    private readonly string _mainDbPath = Path.Combine(Path.GetTempPath(), $"feedback-main-{Guid.NewGuid():N}.db");
    private readonly string _liveDbPath = Path.Combine(Path.GetTempPath(), $"feedback-live-{Guid.NewGuid():N}.db");
    private readonly IOptions<IngestOptions> _options;
    private readonly FeedbackStore _mainStore;
    private readonly FeedbackStore _liveStore;

    public ChannelIsolationTests()
    {
        // Both stores share the SAME IOptions<IngestOptions> instance — the override
        // parameter, not a second options object, is what must separate the databases.
        _options = Options.Create(new IngestOptions { DbPath = _mainDbPath, LiveDbPath = _liveDbPath });
        _mainStore = new FeedbackStore(_options);
        _liveStore = new FeedbackStore(_options, _liveDbPath);
        _mainStore.Initialize();
        _liveStore.Initialize();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_mainDbPath);
        File.Delete(_liveDbPath);
    }

    private IngestService CreateService(FeedbackStore store, FakeStructuring structuring, ReportCache cache) => new(
        store,
        structuring,
        new LlmGate(_options),
        new AlertKeywordSet
        {
            Categories = new Dictionary<string, IReadOnlyList<string>> { ["injury_safety"] = ["loukkaantu"] },
        },
        cache,
        NullLogger<IngestService>.Instance);

    [Fact]
    public async Task DbPathOverride_WritesToAPhysicallyDifferentDatabase_ThanTheDefaultPath()
    {
        var mainItem = MakeStored("main-001", "eka maito");
        await _mainStore.InsertAsync(mainItem, CancellationToken.None);

        // The live store, pointed at a different override path, must not see it...
        Assert.Null(await _liveStore.GetAsync("main-001", CancellationToken.None));
        Assert.Empty(await _liveStore.QueryAsync(null, null, 100, CancellationToken.None));

        var liveItem = MakeStored("live-001", "toka maito");
        await _liveStore.InsertAsync(liveItem, CancellationToken.None);

        // ...and vice versa: the main store never sees the live-only row.
        Assert.Null(await _mainStore.GetAsync("live-001", CancellationToken.None));
        var mainRows = await _mainStore.QueryAsync(null, null, 100, CancellationToken.None);
        Assert.Single(mainRows);
        Assert.Equal("main-001", mainRows[0].Id);

        var liveRows = await _liveStore.QueryAsync(null, null, 100, CancellationToken.None);
        Assert.Single(liveRows);
        Assert.Equal("live-001", liveRows[0].Id);
    }

    [Fact]
    public async Task IngestThroughLiveChannel_InvalidatesLiveCache_LeavesMainCacheUntouched()
    {
        var mainCache = new ReportCache();
        var liveCache = new ReportCache();
        var mainEpochBefore = mainCache.Epoch;
        var liveEpochBefore = liveCache.Epoch;

        var structuring = new FakeStructuring(Success());
        var request = new FeedbackRequest(
            "desk-live-001", "desk", "maito oli vanhaa", "2026-07-01T10:00:00+03:00", ValidStructure, null);

        await CreateService(_liveStore, structuring, liveCache).IngestAsync(request, CancellationToken.None);

        Assert.NotEqual(liveEpochBefore, liveCache.Epoch); // live ingest bumped the live epoch
        Assert.Equal(mainEpochBefore, mainCache.Epoch);    // main cache is a separate instance, untouched

        // Confirm the write actually landed on the live store, not the main one.
        Assert.NotNull(await _liveStore.GetAsync("desk-live-001", CancellationToken.None));
        Assert.Null(await _mainStore.GetAsync("desk-live-001", CancellationToken.None));
    }

    private static StoredFeedback MakeStored(string id, string text) => new(
        id, "desk", text, "2026-07-01T07:00:00.0000000+00:00", "2026-07-01T07:00:00Z",
        ValidStructure, false, false, [], [], null);

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

public class IngestOptionsValidatorLiveDbPathTests
{
    private static IngestOptions Make(string dbPath = "data/feedback.db", string liveDbPath = "data/desk-live.db") =>
        new() { DbPath = dbPath, LiveDbPath = liveDbPath };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespaceLiveDbPath_Fails(string liveDbPath)
    {
        var result = new IngestOptionsValidator().Validate(null, Make(liveDbPath: liveDbPath));

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Ingest:LiveDbPath"));
    }

    [Fact]
    public void LiveDbPath_EqualToDbPath_Fails()
    {
        var options = Make(dbPath: "data/feedback.db", liveDbPath: "data/feedback.db");

        var result = new IngestOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Ingest:LiveDbPath") && f.Contains("Ingest:DbPath"));
    }

    [Fact]
    public void LiveDbPath_DifferentCasingOfSameFile_Fails()
    {
        var options = Make(dbPath: "data/feedback.db", liveDbPath: "DATA/FEEDBACK.DB");

        var result = new IngestOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Ingest:LiveDbPath"));
    }

    [Fact]
    public void LiveDbPath_RelativeVsAbsoluteSpellingOfSameFile_Fails()
    {
        var absoluteDbPath = Path.GetFullPath("data/feedback.db");
        var options = Make(dbPath: "data/feedback.db", liveDbPath: absoluteDbPath);

        var result = new IngestOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Ingest:LiveDbPath"));
    }

    [Fact]
    public void DistinctPaths_Pass()
    {
        var result = new IngestOptionsValidator().Validate(null, Make());

        Assert.False(result.Failed);
    }
}
