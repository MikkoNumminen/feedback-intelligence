using FeedbackIntelligence.Core.Alerts;
using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>ADR-0036: the ONE place that decides which category is forced —
/// the alert lexicon (ADR-0027) always outranks the category-keyword lexicon,
/// and neither ever overrides a demoted (conduct) category already assigned.</summary>
public class CategoryOverrideResolverTests
{
    private static readonly Core.Domain.DomainDescriptor Retail = TestDomains.Retail();
    private static readonly IReadOnlyDictionary<string, CategoryKeywordRule> CategoryKeywords =
        TestDomains.RetailCategoryKeywords().Rules;

    [Fact]
    public void AlertHit_WinsOverProduceTerm()
    {
        var alerts = new List<AlertHit> { new("rasismi", "neeker") };
        var structure = new FeedbackStructure("maito_kylma", "teema", "high", "complaint", "fi");

        var result = CategoryOverrideResolver.Resolve(alerts, "nektariini", structure, Retail, CategoryKeywords);

        Assert.Equal("rasismi", result);
    }

    [Fact]
    public void NoAlert_ProduceTerm_ForcesCategoryKeyword()
    {
        var structure = new FeedbackStructure("maito_kylma", "teema", "high", "complaint", "fi");

        var result = CategoryOverrideResolver.Resolve([], "nektariinierä", structure, Retail, CategoryKeywords);

        Assert.Equal("hevi", result);
    }

    [Fact]
    public void NoAlert_ExistingCategoryIsDemoted_NeverOverridden()
    {
        var structure = new FeedbackStructure("rasismi", "teema", "high", "complaint", "fi");

        var result = CategoryOverrideResolver.Resolve([], "nektariini", structure, Retail, CategoryKeywords);

        Assert.Null(result);
    }

    [Fact]
    public void NoAlert_NoTerm_ReturnsNull()
    {
        var structure = new FeedbackStructure("kassa_palvelu", "teema", "low", "complaint", "fi");

        var result = CategoryOverrideResolver.Resolve([], "pelkkää hyvää palvelua", structure, Retail, CategoryKeywords);

        Assert.Null(result);
    }

    [Fact]
    public void NoAlert_NullStructure_ReturnsNull()
    {
        var result = CategoryOverrideResolver.Resolve([], "nektariini", null, Retail, CategoryKeywords);

        Assert.Null(result);
    }
}
