using System.Globalization;

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
    private static readonly CompareInfo Invariant = CultureInfo.InvariantCulture.CompareInfo;

    /// <summary>Assess text against the lexicon. A stem is counted DISTINCT once even if it
    /// appears in both tiers (dedup), so a repeated single swear can never clear the
    /// distinct gate. The vulgar-character share uses a COVERAGE MASK, so overlapping or
    /// substring stems can never double-count and the ratio can never exceed 1.</summary>
    public static VulgarityAssessment Assess(string text, VulgarityLexicon lex)
    {
        if (lex.IsEmpty || string.IsNullOrWhiteSpace(text))
            return new VulgarityAssessment(0, false);

        var covered = new bool[text.Length];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinct = 0;
        var anyStrong = false;

        foreach (var (stem, strong) in DistinctStems(lex, seen))
            if (MarkOccurrences(text, stem, covered))
            {
                distinct++;
                if (strong) anyStrong = true;
            }

        if (distinct == 0)
            return new VulgarityAssessment(0, false);

        var matchedChars = 0;
        foreach (var c in covered)
            if (c) matchedChars++;
        var nonWs = text.Count(ch => !char.IsWhiteSpace(ch));
        var ratio = nonWs > 0 ? (double)matchedChars / nonWs : 0.0;
        var demote = ratio >= lex.DemoteRatio && distinct >= lex.DemoteMinDistinctStems;
        var level = demote ? 3 : (anyStrong ? 2 : 1);
        return new VulgarityAssessment(level, demote);
    }

    /// <summary>Does the text's vulgarity DENSITY force the demoted conduct category?
    /// The single seam <see cref="CategoryOverrideResolver"/> asks through.</summary>
    public static bool Demotes(string text, VulgarityLexicon lex) =>
        !string.IsNullOrEmpty(lex.DemoteToCategory) && Assess(text, lex).Demote;

    // Mild first, then strong, each stem yielded ONCE across both tiers (a stem listed in
    // both is not double-counted — that would defeat the distinct gate a repeated single
    // swear relies on to stay rated).
    private static IEnumerable<(string Stem, bool Strong)> DistinctStems(VulgarityLexicon lex, HashSet<string> seen)
    {
        foreach (var s in lex.MildStems)
            if (!string.IsNullOrEmpty(s) && seen.Add(s))
                yield return (s, false);
        foreach (var s in lex.StrongStems)
            if (!string.IsNullOrEmpty(s) && seen.Add(s))
                yield return (s, true);
    }

    /// <summary>Mark every case-insensitive invariant occurrence of a stem on the coverage
    /// mask; returns whether the stem matched at least once. Advances by the ACTUAL matched
    /// length (not the stem's length) — culture-aware matching can match a span shorter than
    /// the stem (e.g. an NFD-authored stem against NFC text), so advancing by the stem length
    /// could overshoot the end of the string and throw. Same substring contract as the alert
    /// / category-keyword lexicons; can never over- or under-run the buffer.</summary>
    private static bool MarkOccurrences(string text, string stem, bool[] covered)
    {
        var any = false;
        var stemSpan = stem.AsSpan();
        var idx = 0;
        while (idx < text.Length)
        {
            var rel = Invariant.IndexOf(text.AsSpan(idx), stemSpan, CompareOptions.IgnoreCase, out var matchLen);
            if (rel < 0)
                break;
            any = true;
            var found = idx + rel;
            var end = Math.Min(found + matchLen, text.Length);
            for (var i = found; i < end; i++)
                covered[i] = true;
            idx = found + Math.Max(matchLen, 1);
        }
        return any;
    }
}
