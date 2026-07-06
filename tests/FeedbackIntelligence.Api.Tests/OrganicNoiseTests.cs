using FeedbackIntelligence.Api.Analysis;
using FeedbackIntelligence.Api.Storage;
using FeedbackIntelligence.Core.Structuring;
using Xunit.Abstractions;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>
/// Retail DoD #1 measurement: sweep the trend thresholds over organic noise to
/// pick defaults, and confirm canonical story shapes still detect. Pure — calls
/// ReportService.TrendDirection directly, no store/LLM/GPU.
/// </summary>
public class OrganicNoiseTests(ITestOutputHelper output)
{
    private const string WindowFrom = "2026-06-18T00:00:00.0000000+00:00";
    private const string WindowTo = "2026-07-01T00:00:00.0000000+00:00"; // 13 days

    private static readonly string[] Categories =
    {
        "maito_kylma", "hevi", "kuiva_elintarvike", "liha_kala", "leipa", "kassa_palvelu",
        "piha_puutarha", "rakennustarvike", "tyokalut", "sisustus_maalit", "sahko_lvi",
        "varasto_nouto", "verkkokauppa_toimitus", "muu",
    };
    private static readonly string[] Severities = { "low", "low", "low", "medium", "medium", "medium", "high", "high", "critical" };

    private static double Rank(IReadOnlyList<StoredFeedback> xs) => xs.Count == 0 ? 0 : xs.Average(i =>
        i.Structure!.Severity switch { "low" => 1, "medium" => 2, "high" => 3, "critical" => 4, _ => 2 });

    /// <summary>Organic mass: independent items, uniform-in-time — no real trend
    /// exists, so any non-"stable" direction is a false positive.</summary>
    private static List<StoredFeedback> OrganicWindow(int seed, int count)
    {
        var rng = new Random(seed);
        var from = DateTimeOffset.Parse(WindowFrom);
        var span = (DateTimeOffset.Parse(WindowTo) - from).TotalMinutes;
        var items = new List<StoredFeedback>(count);
        for (var i = 0; i < count; i++)
        {
            var ts = from.AddMinutes(rng.NextDouble() * span).ToString("O");
            items.Add(new StoredFeedback(
                $"noise-{seed}-{i}", "google_review", "organic", ts, ts,
                new FeedbackStructure(Categories[rng.Next(Categories.Length)], "sekalainen",
                    Severities[rng.Next(Severities.Length)], "complaint", "fi"),
                false, false, [], [], null));
        }
        return items;
    }

    /// <summary>Per-category first/second-half split at the window midpoint —
    /// mirrors ReportService.ComputeDirection so the sweep runs on realistic
    /// group shapes.</summary>
    private static IEnumerable<(int First, int Second, double Sf, double Ss)> Splits(IReadOnlyList<StoredFeedback> items)
    {
        var from = DateTimeOffset.Parse(WindowFrom);
        var mid = from + (DateTimeOffset.Parse(WindowTo) - from) / 2;
        foreach (var g in items.GroupBy(i => i.Structure!.Category))
        {
            var first = g.Where(i => DateTimeOffset.Parse(i.Timestamp) < mid).ToList();
            var second = g.Where(i => DateTimeOffset.Parse(i.Timestamp) >= mid).ToList();
            yield return (first.Count, second.Count, Rank(first), Rank(second));
        }
    }

    // The service defaults (ReportOptions.MinItemsForTrend / TrendSignificanceZ).
    private const int DefaultMinItems = 6;
    private const double DefaultZ = 1.6;

    [Fact]
    public void OrganicNoise_ProducesFewFalseTrends_AtDefaults()
    {
        // Independent, uniform-in-time feedback has NO real trend. Under the old
        // 1.25x rule 86% of >=3-item groups were labelled with a false direction;
        // at the significance defaults it must be an order of magnitude lower.
        const int seeds = 40;
        var splits = new List<(int First, int Second, double Sf, double Ss)>();
        for (var s = 1; s <= seeds; s++)
            splits.AddRange(Splits(OrganicWindow(s, 80)));

        var trendable = splits.Count(x => x.First + x.Second >= 3);
        var falseTrends = 0;
        var falseWorsening = 0;
        foreach (var x in splits)
        {
            var dir = ReportService.TrendDirection(x.First, x.Second, x.Sf, x.Ss, DefaultMinItems, DefaultZ);
            if (dir != "stable")
                falseTrends++;
            if (dir == "worsening")
                falseWorsening++;
        }

        var rate = (double)falseTrends / trendable;
        var worseningRate = (double)falseWorsening / trendable;
        output.WriteLine($"false-trend rate: {rate:P1} ({falseTrends}/{trendable}); false-worsening: {worseningRate:P1}");
        Assert.True(rate < 0.10, $"false-trend rate {rate:P1} exceeds 10% — trend logic hallucinates on noise");
        // "worsening" (paheneva) is the alarming label; it must be rare on noise.
        Assert.True(worseningRate < 0.03, $"false-worsening rate {worseningRate:P1} exceeds 3%");
    }

    [Theory]
    // Real story shapes that MUST still be detected (recall preserved).
    [InlineData(2, 10, 1, 3, "worsening")]  // strong: concentrated late + severe
    [InlineData(3, 9, 1, 3, "worsening")]   // moderate: significant at z=1.6
    [InlineData(1, 7, 2, 2, "growing")]     // pure volume growth, flat severity
    [InlineData(0, 6, 0, 1, "growing")]     // brand-new theme, no baseline to worsen
    [InlineData(9, 1, 2, 2, "declining")]   // volume falling off
    // Weak/absent signal that MUST read as stable (no invented trend).
    [InlineData(2, 6, 1, 3, "stable")]      // too weak to distinguish from noise
    [InlineData(6, 6, 2, 2, "stable")]      // balanced
    [InlineData(1, 4, 1, 2, "stable")]      // below minimum volume (n=5)
    public void TrendDirection_AtDefaults_MatchesExpectedShape(
        int first, int second, double sf, double ss, string expected)
    {
        Assert.Equal(expected, ReportService.TrendDirection(first, second, sf, ss, DefaultMinItems, DefaultZ));
    }
}
