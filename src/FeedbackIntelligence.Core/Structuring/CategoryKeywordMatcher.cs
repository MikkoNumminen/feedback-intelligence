namespace FeedbackIntelligence.Core.Structuring;

/// <summary>A category-keyword rule (ADR-0036): a category is FORCED when any of its
/// <paramref name="Terms"/> appears in the raw text, UNLESS one of <paramref name="Exclusions"/>
/// is also present — that marker means the term is a modifier inside another category's
/// compound (produce inside a yogurt / juice / frozen product), so the model keeps its
/// choice. Terms and exclusions are Finnish stems, matched as case-insensitive invariant
/// substrings over the raw text — the same contract as the alert lexicon.</summary>
public sealed record CategoryKeywordRule(IReadOnlyList<string> Terms, IReadOnlyList<string> Exclusions);

/// <summary>Deterministic category assignment from a domain keyword lexicon — a sibling to
/// the alert layer (ADR-0027) but it raises NO alert. Some categories are a finite,
/// enumerable vocabulary (retail's produce → "hevi"), so a wordlist forces them more
/// reliably than an 8B model recalls every name. Runs FIRST and independent of any LLM;
/// the caller applies it only after the alert override and never over a safety/conduct
/// category, and the LLM stays the recall net for names the list misses (ADR-0036).</summary>
public static class CategoryKeywordMatcher
{
    /// <summary>The category to force for <paramref name="text"/>, or null when no rule
    /// fires. A rule fires when one of its terms matches AND none of its exclusions is
    /// present; the first category (enumeration order) whose rule fires wins.</summary>
    public static string? Match(string text, IReadOnlyDictionary<string, CategoryKeywordRule> rules)
    {
        foreach (var (category, rule) in rules)
        {
            // An exclusion marker means a term here is a modifier in another category's
            // compound — leave the category to the model rather than mis-forcing it.
            if (rule.Exclusions.Any(x => text.Contains(x, StringComparison.InvariantCultureIgnoreCase)))
                continue;
            if (rule.Terms.Any(t => text.Contains(t, StringComparison.InvariantCultureIgnoreCase)))
                return category;
        }
        return null;
    }
}
