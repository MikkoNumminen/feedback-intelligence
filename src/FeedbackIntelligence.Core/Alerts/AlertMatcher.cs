using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Core.Alerts;

public sealed record AlertHit(string Category, string Pattern);

/// <summary>
/// The deterministic alert layer's core: case-insensitive invariant substring
/// over the RAW feedback text; patterns are Finnish stems (the exact contract
/// documented in the active domain's alert-keywords.json). Runs FIRST and independent of
/// any LLM — cheap, never sleeps, never hallucinates. The LLM layer may ADD
/// alerts but can never remove a deterministic one.
/// </summary>
public static class AlertMatcher
{
    public static List<AlertHit> Match(string text, IReadOnlyDictionary<string, IReadOnlyList<string>> categories)
    {
        var hits = new List<AlertHit>();
        foreach (var (category, patterns) in categories)
            foreach (var pattern in patterns)
                if (text.Contains(pattern, StringComparison.InvariantCultureIgnoreCase))
                    hits.Add(new AlertHit(category, pattern));
        return hits;
    }

    /// <summary>ADR-0027: an alert-lexicon category whose name IS a declared
    /// structuring category (retail's "rasismi") categorizes the item
    /// deterministically — the forced category outranks the model and desk
    /// acceptance alike, because the lexicon is precision-tuned rule data no
    /// human edits and recognition must not depend on either. First hit whose
    /// category the domain declares wins; null means no override.</summary>
    public static string? CategoryOverride(IReadOnlyList<AlertHit> alerts, IReadOnlySet<string> declaredCategories)
    {
        foreach (var hit in alerts)
            if (declaredCategories.Contains(hit.Category))
                return hit.Category;
        return null;
    }

    /// <summary>The ONE enforcement of <see cref="CategoryOverride"/> on a
    /// structure, shared by ingest, /interpret and both restructure paths so
    /// the preview can never drift from what gets stored: a null structure or
    /// no override returns the input unchanged; otherwise the category is
    /// rewritten (same instance back when it already matches, so callers can
    /// detect "nothing changed" by reference).</summary>
    public static FeedbackStructure? ApplyCategoryOverride(FeedbackStructure? structure, string? overrideCategory) =>
        overrideCategory is not null && structure is not null && structure.Category != overrideCategory
            ? structure with { Category = overrideCategory }
            : structure;

    /// <summary>The distinct alert categories of a hit list, in first-hit order
    /// — the one definition views and reports tag comments with.</summary>
    public static IReadOnlyList<string> DistinctCategories(IEnumerable<AlertHit> alerts) =>
        alerts.Select(a => a.Category).Distinct(StringComparer.Ordinal).ToList();
}
