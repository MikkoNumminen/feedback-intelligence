using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailFeedback.Api;
using RetailFeedback.Api.Analysis;
using RetailFeedback.Api.Storage;
using RetailFeedback.Domain.Structuring;

namespace RetailFeedback.Api.Tests;

public class ReportServiceTests : IDisposable
{
    private const string WindowFrom = "2026-06-18T00:00:00.0000000+00:00";
    private const string WindowTo = "2026-07-01T00:00:00.0000000+00:00";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"report-test-{Guid.NewGuid():N}.db");
    private readonly string _snapshotDir = Path.Combine(Path.GetTempPath(), $"report-snap-{Guid.NewGuid():N}");
    private readonly string _promptPath = Path.Combine(Path.GetTempPath(), $"report-prompt-{Guid.NewGuid():N}.txt");
    private readonly FeedbackStore _store;
    private readonly IOptions<IngestOptions> _ingestOptions;

    public ReportServiceTests()
    {
        _ingestOptions = Options.Create(new IngestOptions { DbPath = _dbPath });
        _store = new FeedbackStore(_ingestOptions);
        _store.Initialize();
        File.WriteAllText(_promptPath, "test prompt\n{{data}}");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        File.Delete(_promptPath);
        if (Directory.Exists(_snapshotDir))
            Directory.Delete(_snapshotDir, true);
    }

    private ReportService CreateService(IChatClient client, bool nominations = false) => new(
        _store,
        client,
        new LlmGate(_ingestOptions),
        Options.Create(new ReportOptions
        {
            SnapshotDir = _snapshotDir,
            SynthesisPromptPath = _promptPath,
            AlertNominationPromptPath = _promptPath,
            AlertNominationEnabled = nominations,
        }),
        NullLogger<ReportService>.Instance);

    private async Task SeedDairyAsync(int earlyCount, int lateCount, string lateSeverity = "high")
    {
        for (var i = 0; i < earlyCount; i++)
            await _store.InsertAsync(Item($"early-{i}", "2026-06-19T10:00:00.0000000+00:00", "low"), CancellationToken.None);
        for (var i = 0; i < lateCount; i++)
            await _store.InsertAsync(Item($"late-{i}", "2026-06-29T10:00:00.0000000+00:00", lateSeverity), CancellationToken.None);
    }

    private static StoredFeedback Item(string id, string timestamp, string severity, IReadOnlyList<Domain.Alerts.AlertHit>? alerts = null) => new(
        id, "desk", $"maito vanhaa {id}", timestamp, timestamp,
        new FeedbackStructure("maito_kylma", "tuotteiden tuoreus", severity, "complaint", "fi"),
        false, false, [], alerts ?? [], null);

    [Fact]
    public async Task Direction_GrowingAndWorsening_IsPaheneva()
    {
        await SeedDairyAsync(earlyCount: 2, lateCount: 6, lateSeverity: "high");

        var report = await CreateService(new ScriptedChatClient("ei-jsonia")).GenerateAsync(WindowFrom, WindowTo, CancellationToken.None);

        var theme = Assert.Single(report.Themes);
        Assert.Equal("paheneva", theme.Direction);
        Assert.Equal(8, theme.Count);
        Assert.Equal(8, theme.FeedbackIds.Count);
    }

    [Fact]
    public async Task GroundedNarrative_IsUsed()
    {
        await SeedDairyAsync(2, 3);
        var llm = new ScriptedChatClient(
            """{"title": "Maidon tuoreus reklamaatioissa", "narrative": "Asiakkaat raportoivat vanhentuneista maitotuotteista.", "citedIds": ["late-0", "early-1"]}""");

        var report = await CreateService(llm).GenerateAsync(WindowFrom, WindowTo, CancellationToken.None);

        var theme = Assert.Single(report.Themes);
        Assert.True(theme.NarrativeFromLlm);
        Assert.Equal("Maidon tuoreus reklamaatioissa", theme.Title);
        Assert.Equal(0, report.DroppedClaimCount);
    }

    [Fact]
    public async Task UngroundedCitation_DropsNarrativeToFallback_AndLogsDrop()
    {
        await SeedDairyAsync(2, 3);
        var llm = new ScriptedChatClient(
            """{"title": "Keksitty", "narrative": "Perusteeton väite.", "citedIds": ["olematon-id"]}""");

        var report = await CreateService(llm).GenerateAsync(WindowFrom, WindowTo, CancellationToken.None);

        var theme = Assert.Single(report.Themes);
        Assert.False(theme.NarrativeFromLlm);
        Assert.Contains("Automaattinen kooste", theme.Narrative);
        Assert.Equal(1, report.DroppedClaimCount);
    }

    [Fact]
    public async Task LlmCompletelyDown_ReportStillGenerates_WithDeterministicLayer()
    {
        await SeedDairyAsync(2, 3);
        await _store.InsertAsync(Item("alert-1", "2026-06-28T10:00:00.0000000+00:00", "critical",
            [new Domain.Alerts.AlertHit("injury_safety", "loukkaantu")]), CancellationToken.None);

        var report = await CreateService(new ThrowingChatClient(), nominations: true)
            .GenerateAsync(WindowFrom, WindowTo, CancellationToken.None);

        Assert.Single(report.Alerts);                       // deterministic alert survives
        Assert.All(report.Themes, t => Assert.False(t.NarrativeFromLlm));
        Assert.Equal(6, report.TotalItems);
    }

    [Fact]
    public async Task AlertNomination_UnknownIdDropped_KnownIdAdded()
    {
        await SeedDairyAsync(1, 2);
        var llm = new ScriptedChatClient(
            """{"alerts": [{"id": "late-0", "reason": "rakenteellinen vika"}, {"id": "vieras-id", "reason": "x"}]}""",
            "ei-jsonia"); // theme synthesis falls back

        var report = await CreateService(llm, nominations: true).GenerateAsync(WindowFrom, WindowTo, CancellationToken.None);

        var alert = Assert.Single(report.Alerts);
        Assert.Equal("late-0", alert.FeedbackId);
        Assert.Equal("rakenteellinen vika", alert.LlmReason);
        Assert.True(report.DroppedClaimCount >= 1); // the unknown id was dropped and counted
    }

    [Fact]
    public async Task Snapshot_IsPersisted_AndReadable()
    {
        await SeedDairyAsync(1, 2);
        var service = CreateService(new ScriptedChatClient("ei-jsonia"));

        await service.GenerateAsync(WindowFrom, WindowTo, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_snapshotDir, "report-latest.json")));
        Assert.True(File.Exists(Path.Combine(_snapshotDir, "report-latest.html")));
        var json = await service.ReadLatestSnapshotJsonAsync(CancellationToken.None);
        Assert.NotNull(json);
        Assert.Contains("maito_kylma", json);
    }

    private sealed class ScriptedChatClient(params string[] responses) : IChatClient
    {
        private int _next;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var text = responses[Math.Min(_next++, responses.Length - 1)];
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("connection refused");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
