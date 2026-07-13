using FeedbackIntelligence.Core.Domain;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>ADR-0026: categoryHints/catchAllCategory are optional domain.json
/// fields validated at load — an unknown key must fail loudly (a typo would
/// otherwise silently guide nothing / never split emergent topics).</summary>
public class ActiveDomainLoadDescriptorTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"domain-test-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    private const string MinimalCategories = """
        "categories": { "a": "A", "b": "B" },
        "sources": ["email"]
        """;

    [Fact]
    public void UnknownCategoryHintsKey_Throws()
    {
        File.WriteAllText(_path, $$"""
            {
              {{MinimalCategories}},
              "categoryHints": { "nope": "not a real category" }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => ActiveDomain.LoadDescriptor(_path, "test"));
        Assert.Contains("categoryHints", ex.Message);
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void UnknownCatchAllCategory_Throws()
    {
        File.WriteAllText(_path, $$"""
            {
              {{MinimalCategories}},
              "catchAllCategory": "nope"
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => ActiveDomain.LoadDescriptor(_path, "test"));
        Assert.Contains("catchAllCategory", ex.Message);
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void ValidFile_ParsesHintsAndCatchAll()
    {
        File.WriteAllText(_path, $$"""
            {
              {{MinimalCategories}},
              "categoryHints": { "a": "hint for a" },
              "catchAllCategory": "b"
            }
            """);

        var descriptor = ActiveDomain.LoadDescriptor(_path, "test");

        Assert.Equal("hint for a", descriptor.CategoryHints["a"]);
        Assert.Equal("b", descriptor.CatchAllCategory);
    }

    [Fact]
    public void FileWithoutThem_YieldsEmptyHintsAndNullCatchAll()
    {
        File.WriteAllText(_path, $$"""
            {
              {{MinimalCategories}}
            }
            """);

        var descriptor = ActiveDomain.LoadDescriptor(_path, "test");

        Assert.Empty(descriptor.CategoryHints);
        Assert.Null(descriptor.CatchAllCategory);
    }
}
