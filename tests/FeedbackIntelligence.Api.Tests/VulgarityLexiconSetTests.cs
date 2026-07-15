using FeedbackIntelligence.Api.Structuring;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>ADR-0039: the vulgarity lexicon is optional domain data, validated at startup —
/// a demoteToCategory that is not a declared, DEMOTED category fails the boot rather than
/// silently re-rating real feedback; a missing file demotes nothing.</summary>
public class VulgarityLexiconSetTests
{
    private static readonly IReadOnlySet<string> Declared = TestDomains.Retail().Categories;
    private static readonly IReadOnlyList<string> Demoted = TestDomains.Retail().DemotedCategories;

    [Fact]
    public void CommittedRetailLexicon_Loads_DemotesToAsiaton()
    {
        var set = TestDomains.RetailVulgarity();
        Assert.Equal("asiaton", set.Lexicon.DemoteToCategory);
        Assert.False(set.Lexicon.IsEmpty);
        Assert.Contains("paska", set.Lexicon.MildStems);
        Assert.Contains("vittu", set.Lexicon.StrongStems);
    }

    [Fact]
    public void MissingFile_ReturnsEmpty_DemotesNothing()
    {
        var set = VulgarityLexiconSet.LoadFrom(
            Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.json"), Declared, Demoted);
        Assert.True(set.Lexicon.IsEmpty);
    }

    [Fact]
    public void DemoteToCategory_NotADemotedCategory_FailsBoot()
    {
        // 'muu' is a declared category but NOT demoted — forcing it would re-rate real feedback.
        var path = Path.Combine(Path.GetTempPath(), $"vulg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"tiers":{"mild":["paska"]},"demoteToCategory":"muu"}""");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => VulgarityLexiconSet.LoadFrom(path, Declared, Demoted));
            Assert.Contains("not a DEMOTED category", ex.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void DemoteToCategory_Undeclared_FailsBoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vulg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"tiers":{"mild":["paska"]},"demoteToCategory":"ei_olemassa"}""");
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => VulgarityLexiconSet.LoadFrom(path, Declared, Demoted));
        }
        finally { File.Delete(path); }
    }
}
