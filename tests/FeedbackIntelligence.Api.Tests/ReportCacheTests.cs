using FeedbackIntelligence.Api.Analysis;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>
/// Pins the cache's lost-invalidation guard (dotnet-audit HIGH). A report generation
/// captures the epoch before it reads items; if a desk ingest invalidates the cache
/// during the ~20 s generation, the trailing Set must NOT resurrect the now-stale
/// report — otherwise "a fresh desk entry appears on the very next refresh" breaks.
/// </summary>
public class ReportCacheTests
{
    private static ManagementReport Report(string tag) =>
        new("2026-06-01", "2026-06-08", "2026-06-08T00:00:00Z", 0, 0, [], [], 0, 0, "fi");

    [Fact]
    public void Set_WithCurrentEpoch_Caches()
    {
        var cache = new ReportCache();
        var epoch = cache.Epoch;

        Assert.True(cache.Set("k", Report("a"), TimeSpan.FromMinutes(1), epoch));
        Assert.True(cache.TryGet("k", out _));
    }

    [Fact]
    public void Invalidate_DuringGeneration_PreventsStaleSet()
    {
        // Sequence: capture epoch (generation starts) -> ingest invalidates -> generation
        // finishes and tries to cache. The Set must no-op, so the next read misses and
        // regenerates with the just-ingested item.
        var cache = new ReportCache();
        var epochAtGenerationStart = cache.Epoch;

        cache.Invalidate(); // a desk POST /feedback lands mid-generation

        Assert.False(cache.Set("k", Report("stale"), TimeSpan.FromMinutes(1), epochAtGenerationStart));
        Assert.False(cache.TryGet("k", out _)); // stale report was NOT cached
    }

    [Fact]
    public void Invalidate_ClearsAnExistingEntry()
    {
        var cache = new ReportCache();
        cache.Set("k", Report("a"), TimeSpan.FromMinutes(1), cache.Epoch);
        Assert.True(cache.TryGet("k", out _));

        cache.Invalidate();

        Assert.False(cache.TryGet("k", out _));
    }
}
