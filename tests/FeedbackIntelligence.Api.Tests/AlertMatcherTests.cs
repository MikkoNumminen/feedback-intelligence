using FeedbackIntelligence.Api.Alerts;
using FeedbackIntelligence.Core.Alerts;

namespace FeedbackIntelligence.Api.Tests;

public class AlertMatcherTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Sample =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["injury_safety"] = ["loukkaantu", "liukastu"],
            ["payment"] = ["maksupäät"],
        };

    [Fact]
    public void Match_FindsStemInInflectedForm()
    {
        var hits = AlertMatcher.Match("Asiakas loukkaantui kaatuneen hyllyn takia", Sample);

        Assert.Contains(hits, h => h is { Category: "injury_safety", Pattern: "loukkaantu" });
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        var hits = AlertMatcher.Match("LIUKASTUIN TEIDÄN LATTIALLA", Sample);

        Assert.Contains(hits, h => h.Pattern == "liukastu");
    }

    [Fact]
    public void Match_FindsMultipleCategories()
    {
        var hits = AlertMatcher.Match("Maksupääte kaatui päälleni ja loukkaannuin... siis loukkaantui käteni", Sample);

        Assert.Contains(hits, h => h.Category == "payment");
        Assert.Contains(hits, h => h.Category == "injury_safety");
    }

    [Fact]
    public void Match_NoKeywords_NoHits()
    {
        var hits = AlertMatcher.Match("Kiitos hyvästä palvelusta!", Sample);

        Assert.Empty(hits);
    }

    /// <summary>
    /// THE demo-critical contract: the no-keyword safety story texts must slip
    /// past the REAL committed keyword list (domains/retail/alert-keywords.json) —
    /// detectable only by understanding. Structural-failure verbs (petti,
    /// irtosi, sortui) are deliberately non-keywords.
    /// </summary>
    [Theory]
    [InlineData("Terassin lauta petti allani kun astuin sille, onneksi ei käynyt pahemmin. Laudat ostettu teiltä keväällä.")]
    [InlineData("Teiltä ostetuista tarvikkeista rakennettu kaide irtosi seinästä kun nojasin siihen.")]
    [InlineData("Keväällä ostetuista laudoista tehty terassi sortui osittain viikonloppuna.")]
    public void RealKeywordConfig_DoesNotFireOnNoKeywordSafetyTexts(string safetyText)
    {
        var keywords = AlertKeywordSet.LoadFrom(Path.Combine(FindRepoRoot(), "domains", "retail", "alert-keywords.json"));

        var hits = AlertMatcher.Match(safetyText, keywords.Categories);

        Assert.Empty(hits);
    }

    [Fact]
    public void RealKeywordConfig_FiresOnInjuryVocabulary()
    {
        var keywords = AlertKeywordSet.LoadFrom(Path.Combine(FindRepoRoot(), "domains", "retail", "alert-keywords.json"));

        var hits = AlertMatcher.Match("Kaaduin märällä lattialla ja jouduin ensiapuun", keywords.Categories);

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal("injury_safety", h.Category));
    }

    internal static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeedbackIntelligence.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found from test bin");
    }
}
