namespace FeedbackIntelligence.Core.Structuring;

/// <summary>The active domain's OPTIONAL graded vulgarity lexicon (ADR-0039): tiered
/// Finnish profanity stems and the DENSITY thresholds that force the demoted conduct
/// category (retail: <c>asiaton</c>). Density-gated on purpose — a lone swear inside real
/// feedback ("Möivät paskaa") stays rated; a pile-up of distinct stems mashed into
/// nonsense ("Paskapillupersepornolehtipaviaani") is non-substantive and demoted. Ethnic
/// slurs are NOT here — they stay in the alert lexicon (ADR-0027), which forces
/// <c>rasismi</c> on a single hit. <see cref="Empty"/> when the domain ships no lexicon
/// (forces nothing), so any domain without the file keeps working.</summary>
public sealed record VulgarityLexicon(
    IReadOnlyList<string> MildStems,
    IReadOnlyList<string> StrongStems,
    string DemoteToCategory,
    // Demote to DemoteToCategory when the vulgar-character SHARE of the message is
    // >= DemoteRatio AND at least DemoteMinDistinctStems DISTINCT stems appear. Both
    // conditions matter: distinct-count (never raw occurrences) keeps a repeated single
    // swear rated; the ratio keeps a furious-but-substantive complaint (mostly real words)
    // rated while a nonsense pile-up (mostly vulgar stems) is demoted.
    double DemoteRatio,
    int DemoteMinDistinctStems)
{
    public static readonly VulgarityLexicon Empty = new([], [], "", 0.4, 2);

    public bool IsEmpty => MildStems.Count == 0 && StrongStems.Count == 0;
}

/// <summary>A graded vulgarity assessment: the <paramref name="Level"/> (0 none · 1
/// incidental mild · 2 notable strong · 3 dominant) and whether it <paramref name="Demote"/>s
/// the message to the demoted conduct category. Only Level 3 demotes; Levels 1–2 are
/// recognized-but-rated (a follow-up ⚑ tag reads them, ADR-0039).</summary>
public readonly record struct VulgarityAssessment(int Level, bool Demote);

/// <summary>Deterministic, density-gated graded vulgarity scoring (ADR-0039) — a sibling to
/// the alert layer (ADR-0027) and the category-keyword layer (ADR-0036), but it recognizes
/// CONDUCT, not a product department, and demotes only on DENSITY so real feedback that
/// merely swears is never hidden. Runs FIRST and independent of any LLM.</summary>
public static class VulgarityScorer
{
    /// <summary>Assess text against the lexicon. Distinct stems are counted once each
    /// (repeating one swear does not inflate the score); the ratio is total matched
    /// characters over the message's non-whitespace length.</summary>
    public static VulgarityAssessment Assess(string text, VulgarityLexicon lex)
    {
        if (lex.IsEmpty || string.IsNullOrWhiteSpace(text))
            return new VulgarityAssessment(0, false);

        var distinct = 0;
        var matchedChars = 0;
        var anyStrong = false;

        foreach (var stem in lex.MildStems)
        {
            var n = CountOccurrences(text, stem);
            if (n == 0) continue;
            distinct++;
            matchedChars += n * stem.Length;
        }
        foreach (var stem in lex.StrongStems)
        {
            var n = CountOccurrences(text, stem);
            if (n == 0) continue;
            distinct++;
            matchedChars += n * stem.Length;
            anyStrong = true;
        }

        if (distinct == 0)
            return new VulgarityAssessment(0, false);

        var nonWs = text.Count(c => !char.IsWhiteSpace(c));
        var ratio = nonWs > 0 ? (double)matchedChars / nonWs : 0.0;
        var demote = ratio >= lex.DemoteRatio && distinct >= lex.DemoteMinDistinctStems;
        var level = demote ? 3 : (anyStrong ? 2 : 1);
        return new VulgarityAssessment(level, demote);
    }

    /// <summary>Does the text's vulgarity DENSITY force the demoted conduct category?
    /// The single seam <see cref="CategoryOverrideResolver"/> asks through.</summary>
    public static bool Demotes(string text, VulgarityLexicon lex) =>
        !string.IsNullOrEmpty(lex.DemoteToCategory) && Assess(text, lex).Demote;

    /// <summary>Non-overlapping, case-insensitive invariant occurrences of a stem — the
    /// same substring contract as the alert / category-keyword lexicons.</summary>
    private static int CountOccurrences(string text, string stem)
    {
        if (string.IsNullOrEmpty(stem))
            return 0;
        var count = 0;
        var idx = 0;
        while ((idx = text.IndexOf(stem, idx, StringComparison.InvariantCultureIgnoreCase)) >= 0)
        {
            count++;
            idx += stem.Length;
        }
        return count;
    }
}
