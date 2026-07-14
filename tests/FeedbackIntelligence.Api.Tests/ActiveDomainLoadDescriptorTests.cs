using FeedbackIntelligence.Core.Domain;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>ADR-0026: categoryHints/catchAllCategory are optional domain.json
/// fields validated at load — an unknown key must fail loudly (a typo would
/// otherwise silently guide nothing / point maintenance at a non-existent bucket).</summary>
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

    // ADR-0030: sentiment (polarity) taxonomy. An explicit typeSentiment is
    // typo-checked on both ends — the key must be a declared type, and the value
    // must be a declared sentiment — mirroring categoryHints/catchAllCategory above.

    [Fact]
    public void TypeSentimentUnknownTypeKey_Throws()
    {
        File.WriteAllText(_path, $$"""
            {
              {{MinimalCategories}},
              "types": { "complaint": "Complaint", "praise": "Praise" },
              "sentiments": { "positive": "Positive", "negative": "Negative" },
              "typeSentiment": { "complaint": "negative", "nope": "positive" }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => ActiveDomain.LoadDescriptor(_path, "test"));
        Assert.Contains("typeSentiment", ex.Message);
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void TypeSentimentUnknownSentimentValue_Throws()
    {
        File.WriteAllText(_path, $$"""
            {
              {{MinimalCategories}},
              "types": { "complaint": "Complaint", "praise": "Praise" },
              "sentiments": { "positive": "Positive", "negative": "Negative" },
              "typeSentiment": { "complaint": "negative", "praise": "ecstatic" }
            }
            """);

        var ex = Assert.Throws<InvalidOperationException>(() => ActiveDomain.LoadDescriptor(_path, "test"));
        Assert.Contains("sentiment", ex.Message);
        Assert.Contains("ecstatic", ex.Message);
    }

    [Fact]
    public void FileWithoutSentiments_YieldsCoreDefaults()
    {
        File.WriteAllText(_path, $$"""
            {
              {{MinimalCategories}}
            }
            """);

        var descriptor = ActiveDomain.LoadDescriptor(_path, "test");

        Assert.Equal("negative", descriptor.SentimentOf("complaint"));
        Assert.Equal("positive", descriptor.SentimentOf("praise"));
        Assert.Equal("neutral", descriptor.SentimentOf("question"));
        Assert.Contains("positive", descriptor.SentimentLabels.Keys);
        Assert.Contains("negative", descriptor.SentimentLabels.Keys);
        Assert.Contains("neutral", descriptor.SentimentLabels.Keys);
    }

    [Fact]
    public void FileWithReducedTypes_AndNoTypeSentiment_DefaultIsIntersectedWithDeclaredTypes()
    {
        // The domain declares only two of the core types — the default typeSentiment
        // must map exactly those (no throw for the ones it dropped), and never
        // invent an entry for a type the domain never declared.
        File.WriteAllText(_path, $$"""
            {
              {{MinimalCategories}},
              "types": { "complaint": "Complaint", "praise": "Praise" }
            }
            """);

        var descriptor = ActiveDomain.LoadDescriptor(_path, "test");

        Assert.Equal("negative", descriptor.SentimentOf("complaint"));
        Assert.Equal("positive", descriptor.SentimentOf("praise"));
        Assert.Null(descriptor.SentimentOf("suggestion")); // never declared by this domain
        Assert.Equal(2, descriptor.TypeSentiment.Count);
    }
}
