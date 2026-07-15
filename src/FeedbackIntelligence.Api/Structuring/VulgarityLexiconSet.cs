using System.Text.Json;
using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Api.Structuring;

/// <summary>The active domain's OPTIONAL graded vulgarity lexicon
/// (domains/&lt;active&gt;/vulgarity-lexicon.json, ADR-0039): tiered Finnish profanity stems
/// and the density thresholds that force the demoted conduct category (retail: asiaton).
/// Optional by design — a domain without the file demotes nothing (empty lexicon), so the
/// game domain and any future domain keep working. When the file IS present it is validated
/// at startup: a missing/blank <c>demoteToCategory</c>, one that is not a declared DEMOTED
/// category, or a tiers object with no stems fails the boot rather than silently disabling
/// the layer.</summary>
public sealed class VulgarityLexiconSet
{
    public required VulgarityLexicon Lexicon { get; init; }

    public static readonly VulgarityLexiconSet Empty = new() { Lexicon = VulgarityLexicon.Empty };

    public static VulgarityLexiconSet LoadFrom(
        string path, IReadOnlySet<string> declaredCategories, IReadOnlyList<string> demotedCategories)
    {
        var resolved = FeedbackIntelligence.Llm.AppPathResolver.Resolve(path);
        if (!File.Exists(resolved))
            return Empty; // optional layer: no file → demote nothing

        using var doc = JsonDocument.Parse(File.ReadAllText(resolved));
        var root = doc.RootElement;

        if (!root.TryGetProperty("demoteToCategory", out var demoteEl)
            || demoteEl.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(demoteEl.GetString()))
            throw new InvalidOperationException($"'{resolved}' must set a non-empty 'demoteToCategory'.");
        var demoteTo = demoteEl.GetString()!;
        // Forcing an undeclared or non-demoted category would either write an invalid value
        // or (worse) re-rate real feedback — fail the boot, like the category-keyword checks.
        if (!declaredCategories.Contains(demoteTo))
            throw new InvalidOperationException(
                $"'demoteToCategory' \"{demoteTo}\" in '{resolved}' is not a declared category.");
        if (!demotedCategories.Contains(demoteTo))
            throw new InvalidOperationException(
                $"'demoteToCategory' \"{demoteTo}\" in '{resolved}' is not a DEMOTED category — vulgarity may only "
                + "force a conduct/moderation category (ADR-0039).");

        var mild = ReadTier(root, "mild");
        var strong = ReadTier(root, "strong");
        if (mild.Count == 0 && strong.Count == 0)
            throw new InvalidOperationException($"'{resolved}' has no vulgarity stems under 'tiers.mild' / 'tiers.strong'.");

        var ratio = ReadDouble(root, "demoteRatio", VulgarityLexicon.Empty.DemoteRatio);
        var minDistinct = ReadInt(root, "demoteMinDistinctStems", VulgarityLexicon.Empty.DemoteMinDistinctStems);
        if (ratio is <= 0 or > 1)
            throw new InvalidOperationException($"'demoteRatio' in '{resolved}' must be in (0, 1].");
        if (minDistinct < 1)
            throw new InvalidOperationException($"'demoteMinDistinctStems' in '{resolved}' must be >= 1.");

        return new VulgarityLexiconSet
        {
            Lexicon = new VulgarityLexicon(mild, strong, demoteTo, ratio, minDistinct),
        };
    }

    private static IReadOnlyList<string> ReadTier(JsonElement root, string tier) =>
        root.TryGetProperty("tiers", out var tiers) && tiers.ValueKind == JsonValueKind.Object
        && tiers.TryGetProperty(tier, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList()
            : [];

    private static double ReadDouble(JsonElement root, string prop, double fallback) =>
        root.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : fallback;

    private static int ReadInt(JsonElement root, string prop, int fallback) =>
        root.TryGetProperty(prop, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : fallback;
}
