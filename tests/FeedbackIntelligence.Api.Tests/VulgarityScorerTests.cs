using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>ADR-0039: density-gated graded vulgarity. Demote (Level 3 → asiaton) only when
/// the vulgar-character share is high AND enough DISTINCT stems appear — so a lone swear in
/// real feedback, or a repeated single swear, stays rated, while a nonsense pile-up is
/// demoted. Uses the COMMITTED retail lexicon so the tuned thresholds are what ships.</summary>
public class VulgarityScorerTests
{
    private static readonly VulgarityLexicon Lex = TestDomains.RetailVulgarity().Lexicon;

    [Fact]
    public void DensePileUp_OfDistinctStems_Demotes()
    {
        // paska + pillu + perse = 3 distinct, ~0.45 vulgar share → Level 3 → asiaton.
        var a = VulgarityScorer.Assess("Paskapillupersepornolehtipaviaani", Lex);
        Assert.True(a.Demote);
        Assert.Equal(3, a.Level);
        Assert.True(VulgarityScorer.Demotes("Paskapillupersepornolehtipaviaani", Lex));
    }

    [Fact]
    public void LoneSwearInRealComplaint_DoesNotDemote()
    {
        // one distinct stem, low share → a crude-but-real complaint stays rated.
        Assert.False(VulgarityScorer.Assess("Möivät paskaa. Epäilyttävää paskaa.", Lex).Demote);
    }

    [Fact]
    public void RepeatedSingleStem_StaysRated_DistinctGate()
    {
        // "singletons shouldn't set a big flag" — even repeated, ONE distinct stem never demotes.
        Assert.False(VulgarityScorer.Demotes("paska paska paska paska", Lex));
    }

    [Fact]
    public void FuriousButSubstantiveComplaint_WithThreeSwears_StaysRated()
    {
        // Three DISTINCT stems (paska/kyrpä/perse) but embedded in real words → low vulgar
        // share → rated. This is the case raw distinct-count alone would wrongly demote.
        const string text = "Tämä tuote on ihan paska ja teidän palvelunne on kyrpää, en osta enää mitään perseestä.";
        Assert.False(VulgarityScorer.Assess(text, Lex).Demote);
    }

    [Fact]
    public void ContentFreeTwoStemWall_Demotes()
    {
        // no substance, all vulgar → demote even at two distinct stems.
        Assert.True(VulgarityScorer.Demotes("vittu perse", Lex));
    }

    [Fact]
    public void CleanText_IsLevelZero_NoDemote()
    {
        var a = VulgarityScorer.Assess("Kurpitsojen laatu vaikuttaa olevan parempi nyt", Lex);
        Assert.Equal(0, a.Level);
        Assert.False(a.Demote);
    }

    [Fact]
    public void EmptyLexicon_NeverDemotes()
    {
        Assert.False(VulgarityScorer.Demotes("paska perse vittu mulkku", VulgarityLexicon.Empty));
    }

    [Fact]
    public void NfdAuthoredStem_MatchesNfcText_WithoutOverrunningTheBuffer()
    {
        // A stem authored in DECOMPOSED form: 'kyrp' + 'a' + U+0308 combining diaeresis = 6 code
        // units, canonically equal to the composed 'kyrp' + U+00E4. Matched against an NFC
        // message ending in the composed form (matched span = 5 units), culture-aware matching
        // spans FEWER units than the stem, so advancing by the STEM length would index past the
        // end and throw. Must still match (canonical equivalence) and must not crash. Built from
        // (char) casts so the normalization form is exact regardless of source-file encoding.
        var nfdStem = "kyrp" + 'a' + (char)0x0308;   // decomposed
        var nfcText = "paska kyrp" + (char)0x00e4;   // composed, match at the end of the string
        var lex = new VulgarityLexicon(["paska"], [nfdStem], "asiaton", 0.4, 2);
        Assert.True(VulgarityScorer.Demotes(nfcText, lex));
    }

    [Fact]
    public void StemListedInBothTiers_IsCountedOnce_RepeatedSwearStaysRated()
    {
        // A copy-paste duplicate across tiers must NOT inflate the distinct-count — otherwise a
        // repeated single swear ("paska paska") would clear the distinct gate and be demoted,
        // the exact case ADR-0039 says stays rated.
        var lex = new VulgarityLexicon(["paska"], ["paska"], "asiaton", 0.4, 2);
        Assert.False(VulgarityScorer.Demotes("paska paska", lex));
    }
}
