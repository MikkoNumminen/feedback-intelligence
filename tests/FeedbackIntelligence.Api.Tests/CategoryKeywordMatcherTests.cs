using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>ADR-0036: the committed retail category-keyword lexicon
/// (domains/retail/category-keywords.json) forces a category for finite,
/// enumerable product-noun vocabulary the model sometimes misses (produce),
/// while a derivative head word (jogurtti, suklaa, kakku, puikko, mehu,
/// jäätelö) routes the compound to the department it actually is.</summary>
public class CategoryKeywordMatcherTests
{
    private static readonly IReadOnlyDictionary<string, CategoryKeywordRule> Rules =
        TestDomains.RetailCategoryKeywords().Rules;

    [Fact]
    public void BareProduceNoun_ForcesHevi()
    {
        Assert.Equal("hevi", CategoryKeywordMatcher.Match("nektariinierä oli homeessa", Rules));
    }

    [Fact]
    public void AnotherBareProduceNoun_ForcesHevi()
    {
        Assert.Equal("hevi", CategoryKeywordMatcher.Match("banaani oli mustunut", Rules));
    }

    [Fact]
    public void ProduceWithJogurttiHead_RoutesToDairy()
    {
        // hevi excludes 'jogurtti'; dairy has it.
        Assert.Equal("maito_kylma", CategoryKeywordMatcher.Match("nektariinijogurtti oli hapan", Rules));
    }

    [Fact]
    public void MilkChocolate_RoutesToCandy_BeforeDairy()
    {
        // makeiset before maito_kylma in declaration order ('suklaa').
        Assert.Equal("makeiset", CategoryKeywordMatcher.Match("maitosuklaa suli", Rules));
    }

    [Fact]
    public void CheeseCake_RoutesToBakery()
    {
        // dairy excludes 'kakku'; bakery has it.
        Assert.Equal("leipa", CategoryKeywordMatcher.Match("juustokakku oli kuiva", Rules));
    }

    [Fact]
    public void FishStick_RoutesToFrozen()
    {
        // meat excludes 'puikko'; frozen has 'kalapuikko'.
        Assert.Equal("pakasteet", CategoryKeywordMatcher.Match("kalapuikko oli sitkeä", Rules));
    }

    [Fact]
    public void AppleJuice_RoutesToDrinks()
    {
        // hevi excludes 'mehu'; drinks have it.
        Assert.Equal("juomat", CategoryKeywordMatcher.Match("omenamehu loppui", Rules));
    }

    [Fact]
    public void ChocolateIceCream_RoutesToFrozen_BeforeBakery()
    {
        // makeiset excludes 'jäätelö'; frozen has it, before bakery in declaration order.
        Assert.Equal("pakasteet", CategoryKeywordMatcher.Match("suklaajäätelö oli sulanut", Rules));
    }

    [Fact]
    public void NoProductOrServiceNoun_ReturnsNull()
    {
        // ADR-0037: 'myyjä oli töykeä' now forces kassa_palvelu (service fallback), so this
        // needs a text with neither a product noun nor a service/premises noun to stay null.
        Assert.Null(CategoryKeywordMatcher.Match("Kokemus oli yleisesti pettymys.", Rules));
    }

    [Fact]
    public void EmptyText_ReturnsNull()
    {
        Assert.Null(CategoryKeywordMatcher.Match("", Rules));
    }

    [Fact]
    public void Match_IsCaseInsensitive()
    {
        Assert.Equal("hevi", CategoryKeywordMatcher.Match("NEKTARIINI", Rules));
    }

    // ADR-0037: kassa_palvelu and tilat_siisteys are the service/premises FALLBACK,
    // declared LAST so a product noun always wins over a service/premises word.

    [Fact]
    public void PureServiceComment_NoProductNoun_ForcesKassaPalvelu()
    {
        Assert.Equal(
            "kassa_palvelu",
            CategoryKeywordMatcher.Match(
                "Tuli hyvä mieli kun myyjä oli energinen ja neuvoi missä wc-tilat sijaitsevat.", Rules));
    }

    [Fact]
    public void ServiceWordWithProductNoun_ProductWins()
    {
        Assert.Equal("maito_kylma", CategoryKeywordMatcher.Match("Myyjä sanoi että maito oli vanhaa.", Rules));
    }

    [Fact]
    public void CashierQueue_ForcesKassaPalvelu()
    {
        Assert.Equal("kassa_palvelu", CategoryKeywordMatcher.Match("Jono kassalla oli aivan liian pitkä.", Rules));
    }

    [Fact]
    public void CustomerService_ForcesKassaPalvelu()
    {
        Assert.Equal("kassa_palvelu", CategoryKeywordMatcher.Match("Asiakaspalvelu ei toiminut ollenkaan.", Rules));
    }

    [Fact]
    public void DirtyRestrooms_ForcesTilatSiisteys()
    {
        Assert.Equal("tilat_siisteys", CategoryKeywordMatcher.Match("Vessat olivat todella likaiset.", Rules));
    }

    [Fact]
    public void ParkingLotCleanliness_ForcesTilatSiisteys()
    {
        Assert.Equal(
            "tilat_siisteys",
            CategoryKeywordMatcher.Match("Parkkipaikan siivous on ollut puutteellista.", Rules));
    }

    [Fact]
    public void ShoppingCarts_ForceTilatSiisteys()
    {
        Assert.Equal("tilat_siisteys", CategoryKeywordMatcher.Match("Ostoskärryt olivat rikki.", Rules));
    }

    [Fact]
    public void ServiceWordWithProductLocationMention_ProductStillWins()
    {
        // Documented residual (ADR-0037): a product-LOCATION compound (maitohyllyllä, the
        // dairy AISLE) still names the base product term 'maito', so it forces maito_kylma
        // even though the sentence is really about the employee's manner, not the milk
        // itself. Accepted trade-off, pinned here so it's intentional, not a regression.
        Assert.Equal("maito_kylma", CategoryKeywordMatcher.Match("Myyjä maitohyllyllä oli töykeä.", Rules));
    }

    [Fact]
    public void ProductNounStillWorks_Sanity()
    {
        Assert.Equal("hevi", CategoryKeywordMatcher.Match("Ostamani banaani oli mustunut.", Rules));
    }
}
