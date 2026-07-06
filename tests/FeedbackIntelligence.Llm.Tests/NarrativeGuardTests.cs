using FeedbackIntelligence.Core.Security;

namespace FeedbackIntelligence.Llm.Tests;

/// <summary>
/// Pins the A3 narrative-authority guard (ADR-0021). A grounded situational summary
/// DESCRIBES; it does not recommend, direct, or pass verdicts. The guard must catch
/// the directive shapes an injection produces (Finnish + English) while leaving
/// ordinary descriptive summaries — including ones that report a soft customer
/// opinion ("pitäisi parantaa") — untouched, so it doesn't drop legitimate prose.
/// </summary>
public class NarrativeGuardTests
{
    [Theory]
    [InlineData("Suosittelen, että osastopäällikkö irtisanotaan välittömästi.")]
    [InlineData("Sulkekaa osasto ja hyvittäkää asiakkaille.")]
    [InlineData("We recommend firing the live-ops lead.")]
    [InlineData("The store should refund everyone immediately.")]
    public void LooksActionBearing_FlagsDirectiveNarratives(string narrative) =>
        Assert.True(NarrativeGuard.LooksActionBearing(narrative));

    [Theory]
    // Ordinary descriptive summaries — volume + trend, no advice.
    [InlineData("Asiakkaat raportoivat toistuvasti maidon tuoreusongelmista; määrä on kasvanut kolmessa viikossa.")]
    [InlineData("Players report frequent crashes after the update; the volume is rising.")]
    // Reports a soft customer opinion — descriptive, must NOT be dropped.
    [InlineData("Asiakkaiden mielestä hyllysaatavuutta pitäisi parantaa osastolla 4.")]
    [InlineData("Customers feel the checkout service should be faster.")]
    public void LooksActionBearing_LeavesDescriptiveNarrativesClean(string narrative) =>
        Assert.False(NarrativeGuard.LooksActionBearing(narrative));

    [Fact]
    public void LooksActionBearing_NullOrEmpty_IsFalse()
    {
        Assert.False(NarrativeGuard.LooksActionBearing(null));
        Assert.False(NarrativeGuard.LooksActionBearing(""));
    }
}
