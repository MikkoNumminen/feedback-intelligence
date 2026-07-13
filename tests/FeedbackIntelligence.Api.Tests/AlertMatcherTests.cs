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
        var keywords = TestDomains.RetailKeywords();

        var hits = AlertMatcher.Match(safetyText, keywords.Categories);

        Assert.Empty(hits);
    }

    [Fact]
    public void RealKeywordConfig_FiresOnInjuryVocabulary()
    {
        var keywords = TestDomains.RetailKeywords();

        var hits = AlertMatcher.Match("Kaaduin märällä lattialla ja jouduin ensiapuun", keywords.Categories);

        Assert.NotEmpty(hits);
        Assert.All(hits, h => Assert.Equal("injury_safety", h.Category));
    }

    [Theory]
    [InlineData("Jumalauta, NEEKERIT sikiää täällä")]
    [InlineData("Näin rättipään teidän kassalla")]
    [InlineData("Kassalla huudettiin sieg heil")]
    public void RealKeywordConfig_RasismiStems_MatchInflectedForms(string text)
    {
        var keywords = TestDomains.RetailKeywords();

        var hits = AlertMatcher.Match(text, keywords.Categories);

        Assert.Contains(hits, h => h.Category == "rasismi");
    }

    /// <summary>The lexicon's documented precision calls (deliberateExclusions):
    /// stems that would false-positive on ordinary Finnish are NOT in the list.</summary>
    [Theory]
    [InlineData("Kävin Brysselissä lomalla, tuliaiset ostin teiltä")]     // 'ryss' inside Bryssel — excluded stem shape
    [InlineData("Yrityksen maturiteetti on korkealla tasolla")]           // 'matu' — deliberately excluded
    public void RealKeywordConfig_RasismiStems_DoNotFireOnDocumentedExclusions(string text)
    {
        var keywords = TestDomains.RetailKeywords();

        var hits = AlertMatcher.Match(text, keywords.Categories);

        Assert.DoesNotContain(hits, h => h.Category == "rasismi");
    }

    [Fact]
    public void CategoryOverride_ReturnsFirstAlertThatIsADeclaredCategory()
    {
        var retail = TestDomains.Retail();
        var alerts = new List<AlertHit> { new("injury_safety", "loukkaantu"), new("rasismi", "neeker") };

        // "injury_safety" is alert-only vocabulary, not a structuring category —
        // the override skips past it to "rasismi", which is both (ADR-0027).
        Assert.Equal("rasismi", AlertMatcher.CategoryOverride(alerts, retail.Categories));
        Assert.Null(AlertMatcher.CategoryOverride(
            [new AlertHit("injury_safety", "loukkaantu")], retail.Categories));
        Assert.Null(AlertMatcher.CategoryOverride([], retail.Categories));
    }

}
