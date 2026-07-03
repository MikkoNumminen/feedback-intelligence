using System.Text.Json;

namespace RetailFeedback.Api.Alerts;

/// <summary>
/// The committed keyword config (config/alert-keywords.json), loaded and
/// validated at startup — a malformed or empty list must fail the boot, not
/// silently disable the deterministic alert layer.
/// </summary>
public sealed class AlertKeywordSet
{
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Categories { get; init; }

    public static AlertKeywordSet LoadFrom(string path)
    {
        var resolved = RetailFeedback.Llm.AppPathResolver.Resolve(path);
        if (!File.Exists(resolved))
            throw new InvalidOperationException(
                $"Alert keyword config not found: '{path}' (cwd: {Environment.CurrentDirectory}).");

        using var doc = JsonDocument.Parse(File.ReadAllText(resolved));
        if (!doc.RootElement.TryGetProperty("categories", out var categoriesElement)
            || categoriesElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"'{resolved}' must contain a 'categories' object.");

        var categories = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var category in categoriesElement.EnumerateObject())
        {
            if (category.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"Alert category '{category.Name}' must be an array of patterns.");
            var patterns = category.Value.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            if (patterns.Count == 0)
                throw new InvalidOperationException($"Alert category '{category.Name}' has no usable patterns.");
            categories[category.Name] = patterns;
        }
        if (categories.Count == 0)
            throw new InvalidOperationException($"'{resolved}' defines no alert categories.");

        return new AlertKeywordSet { Categories = categories };
    }
}
