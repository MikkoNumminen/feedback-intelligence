using FeedbackIntelligence.Core.Alerts;
using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>ADR-0036/0039: the ONE place that decides which category is forced —
/// the alert lexicon (ADR-0027, racism) always outranks dense vulgarity (ADR-0039),
/// which outranks the category-keyword lexicon (ADR-0036), and none ever overrides a
/// demoted (conduct) category already assigned.</summary>
public class CategoryOverrideResolverTests
{
    private static readonly Core.Domain.DomainDescriptor Retail = TestDomains.Retail();
    private static readonly IReadOnlyDictionary<string, CategoryKeywordRule> CategoryKeywords =
        TestDomains.RetailCategoryKeywords().Rules;
    private static readonly VulgarityLexicon Vulgarity = TestDomains.RetailVulgarity().Lexicon;

    [Fact]
    public void AlertHit_WinsOverProduceTerm()
    {
        var alerts = new List<AlertHit> { new("rasismi", "neeker") };
        var structure = new FeedbackStructure("maito_kylma", "teema", "high", "complaint", "fi");

        var result = CategoryOverrideResolver.Resolve(alerts, "nektariini", structure, Retail, CategoryKeywords, Vulgarity);

        Assert.Equal("rasismi", result);
    }

    [Fact]
    public void NoAlert_ProduceTerm_ForcesCategoryKeyword()
    {
        var structure = new FeedbackStructure("maito_kylma", "teema", "high", "complaint", "fi");

        var result = CategoryOverrideResolver.Resolve([], "nektariinierä", structure, Retail, CategoryKeywords, Vulgarity);

        Assert.Equal("hevi", result);
    }

    [Fact]
    public void NoAlert_ExistingCategoryIsDemoted_NeverOverridden()
    {
        var structure = new FeedbackStructure("rasismi", "teema", "high", "complaint", "fi");

        var result = CategoryOverrideResolver.Resolve([], "nektariini", structure, Retail, CategoryKeywords, Vulgarity);

        Assert.Null(result);
    }

    [Fact]
    public void NoAlert_NoTerm_ReturnsNull()
    {
        var structure = new FeedbackStructure("kassa_palvelu", "teema", "low", "complaint", "fi");

        // ADR-0037: 'palvelu' is now itself a kassa_palvelu term, so this needs text with
        // no category-keyword term at all (product OR service/premises) to stay null.
        var result = CategoryOverrideResolver.Resolve([], "ihan tavallinen kokemus", structure, Retail, CategoryKeywords, Vulgarity);

        Assert.Null(result);
    }

    [Fact]
    public void NoAlert_NullStructure_ReturnsNull()
    {
        var result = CategoryOverrideResolver.Resolve([], "nektariini", null, Retail, CategoryKeywords, Vulgarity);

        Assert.Null(result);
    }

    [Fact]
    public void DenseVulgarity_ForcesAsiaton_OverModelCategory()
    {
        // ADR-0039: a pile-up of distinct vulgar stems mashed into nonsense — the model
        // filed it under the rated catch-all 'muu'; the density override moves it to asiaton.
        var structure = new FeedbackStructure("muu", "offensive_language", "high", "other", "fi");

        var result = CategoryOverrideResolver.Resolve(
            [], "Paskapillupersepornolehtipaviaani", structure, Retail, CategoryKeywords, Vulgarity);

        Assert.Equal("asiaton", result);
    }

    [Fact]
    public void LoneSwearInRealComplaint_StaysRated_NotDemoted()
    {
        // ADR-0039: one stem inside substantive feedback is a crude-but-real complaint —
        // density is too low to demote, so it keeps its rated category (no override here).
        var structure = new FeedbackStructure("muu", "laatu", "high", "complaint", "fi");

        var result = CategoryOverrideResolver.Resolve(
            [], "Möivät paskaa. Epäilyttävää paskaa.", structure, Retail, CategoryKeywords, Vulgarity);

        Assert.Null(result); // no override → the model's rated category stands
    }

    [Fact]
    public void RacismAlert_WinsOverDenseVulgarity()
    {
        // Racism (single-hit alert, ADR-0027) outranks the vulgarity density override:
        // a message that is BOTH a slur-alert and dense vulgarity is filed rasismi, not asiaton.
        var alerts = new List<AlertHit> { new("rasismi", "neeker") };
        var structure = new FeedbackStructure("muu", "teema", "high", "other", "fi");

        var result = CategoryOverrideResolver.Resolve(
            alerts, "neekeri paska vittu perse", structure, Retail, CategoryKeywords, Vulgarity);

        Assert.Equal("rasismi", result);
    }
}
