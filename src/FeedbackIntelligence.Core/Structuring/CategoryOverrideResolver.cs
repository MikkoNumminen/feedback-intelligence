using FeedbackIntelligence.Core.Alerts;
using FeedbackIntelligence.Core.Domain;

namespace FeedbackIntelligence.Core.Structuring;

/// <summary>The ONE place that resolves which category a deterministic rule forces, so
/// ingest, /interpret and restructure can never disagree about what gets stored. The
/// alert lexicon wins first (ADR-0027 — a safety/conduct category outranks the model,
/// the desk, and everything else); only if it is silent does the category-keyword lexicon
/// apply (ADR-0036 — retail's produce → hevi), and never over a demoted (conduct) category,
/// because produce forcing is a categorization aid, not a conduct signal. Null = keep the
/// model's category.</summary>
public static class CategoryOverrideResolver
{
    public static string? Resolve(
        IReadOnlyList<AlertHit> alerts,
        string text,
        FeedbackStructure? structure,
        DomainDescriptor descriptor,
        IReadOnlyDictionary<string, CategoryKeywordRule> categoryKeywords)
    {
        var alertOverride = AlertMatcher.CategoryOverride(alerts, descriptor.Categories);
        if (alertOverride is not null)
            return alertOverride;
        if (structure is null || descriptor.DemotedCategories.Contains(structure.Category))
            return null;
        return CategoryKeywordMatcher.Match(text, categoryKeywords);
    }
}
