using System.Text.Json;
using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Api.Structuring;

/// <summary>The active domain's OPTIONAL category-keyword lexicon
/// (domains/&lt;active&gt;/category-keywords.json, ADR-0036): term lists that force a category
/// deterministically (retail's produce → hevi). Optional by design — a domain without the
/// file forces nothing (empty rules), so the game domain and any future domain keep working.
/// When the file IS present it is validated at startup: an unknown category key, a missing
/// 'terms' list, or an empty one fails the boot rather than silently disabling the layer.</summary>
public sealed class CategoryKeywordSet
{
    public required IReadOnlyDictionary<string, CategoryKeywordRule> Rules { get; init; }

    public static readonly CategoryKeywordSet Empty = new()
    {
        Rules = new Dictionary<string, CategoryKeywordRule>(StringComparer.Ordinal),
    };

    public static CategoryKeywordSet LoadFrom(string path, IReadOnlySet<string> declaredCategories)
    {
        var resolved = FeedbackIntelligence.Llm.AppPathResolver.Resolve(path);
        if (!File.Exists(resolved))
            return Empty; // optional layer: no file → force nothing

        using var doc = JsonDocument.Parse(File.ReadAllText(resolved));
        if (!doc.RootElement.TryGetProperty("categories", out var categoriesElement)
            || categoriesElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"'{resolved}' must contain a 'categories' object.");

        var rules = new Dictionary<string, CategoryKeywordRule>(StringComparer.Ordinal);
        foreach (var category in categoriesElement.EnumerateObject())
        {
            // Forcing an undeclared category would write an invalid value — fail the boot,
            // mirroring the domain.json typo checks for categoryHints/catchAllCategory.
            if (!declaredCategories.Contains(category.Name))
                throw new InvalidOperationException(
                    $"Category-keyword '{category.Name}' in '{resolved}' is not a declared category.");
            if (category.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"Category-keyword '{category.Name}' must be an object with 'terms'.");
            var terms = ReadStrings(category.Value, "terms");
            if (terms.Count == 0)
                throw new InvalidOperationException($"Category-keyword '{category.Name}' has no usable 'terms'.");
            rules[category.Name] = new CategoryKeywordRule(terms, ReadStrings(category.Value, "excludeIfContains"));
        }
        return new CategoryKeywordSet { Rules = rules };
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement obj, string prop) =>
        obj.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList()
            : [];
}
