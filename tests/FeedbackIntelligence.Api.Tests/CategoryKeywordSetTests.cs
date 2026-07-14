using FeedbackIntelligence.Api.Structuring;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>ADR-0036: the category-keyword lexicon is an OPTIONAL layer (a
/// domain without the file forces nothing) but validated when present — an
/// undeclared category key or an empty term list fails the boot.</summary>
public class CategoryKeywordSetTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"category-keywords-test-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void RealRetailLexicon_LoadsNineCategories_HeviHasNektariini()
    {
        // ADR-0037 added kassa_palvelu and tilat_siisteys to the 7 product departments.
        var set = CategoryKeywordSet.LoadFrom(
            Path.Combine(TestDomains.RepoRoot(), "domains", "retail", "category-keywords.json"),
            TestDomains.Retail().Categories);

        Assert.Equal(9, set.Rules.Count);
        Assert.True(set.Rules.ContainsKey("hevi"));
        Assert.Contains("nektariini", set.Rules["hevi"].Terms);
    }

    [Fact]
    public void Rules_PreserveJsonDeclarationOrder()
    {
        // The cross-category exclusions route compounds on their own; declaration order is
        // only the tie-break when a text names two departments. Pin it so a future refactor
        // of LoadFrom can't silently reshuffle precedence (ADR-0036).
        var set = CategoryKeywordSet.LoadFrom(
            Path.Combine(TestDomains.RepoRoot(), "domains", "retail", "category-keywords.json"),
            TestDomains.Retail().Categories);

        // Product departments FIRST, service/premises LAST (ADR-0037): a product word
        // always wins; service is the fallback for comments with no product noun.
        Assert.Equal(
            new[] { "hevi", "makeiset", "maito_kylma", "liha_kala", "juomat", "pakasteet", "leipa",
                    "kassa_palvelu", "tilat_siisteys" },
            set.Rules.Keys.ToArray());
    }

    [Fact]
    public void MissingFile_ReturnsEmpty_DoesNotThrow()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"no-such-file-{Guid.NewGuid():N}.json");

        var set = CategoryKeywordSet.LoadFrom(missingPath, TestDomains.Retail().Categories);

        Assert.Same(CategoryKeywordSet.Empty, set);
        Assert.Empty(set.Rules);
    }

    [Fact]
    public void UndeclaredCategory_Throws()
    {
        File.WriteAllText(_path, """
            {
              "categories": {
                "nope": { "terms": ["x"] }
              }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(
            () => CategoryKeywordSet.LoadFrom(_path, TestDomains.Retail().Categories));

        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void EmptyTermsList_Throws()
    {
        File.WriteAllText(_path, """
            {
              "categories": {
                "hevi": { "terms": [] }
              }
            }
            """);

        Assert.Throws<InvalidOperationException>(
            () => CategoryKeywordSet.LoadFrom(_path, TestDomains.Retail().Categories));
    }
}
