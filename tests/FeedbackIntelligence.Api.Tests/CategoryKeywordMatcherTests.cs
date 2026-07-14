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
    public void NoProductNoun_ReturnsNull()
    {
        Assert.Null(CategoryKeywordMatcher.Match("myyjä oli töykeä", Rules));
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
}
