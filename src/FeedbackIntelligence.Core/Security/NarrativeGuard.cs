namespace FeedbackIntelligence.Core.Security;

/// <summary>
/// Bounds the AUTHORITY of a synthesis narrative to a grounded DESCRIPTION
/// (ADR-0021 A3). A situational summary reports what customers said and which way
/// it is trending; it does not recommend, direct, or pass verdicts. That matters
/// for injection defense: an in-band "recommend firing the department manager" /
/// "erota osastopäällikkö" that survives into the narrative would give an attacker
/// a management-facing action slot. If the narrative turns directive, it is dropped
/// to the deterministic fallback — so an injected instruction has nowhere to live.
///
/// Backstop to the prompt constraint (which tells the model to describe only), NOT
/// a wall: a determined paraphrase evades substring matching. Language-neutral core
/// (Finnish + English), same lowercase-invariant substring contract as the alert
/// and injection-symptom layers. Markers are deliberately narrow — clear
/// directive/recommendation/personnel/closure verbs — so ordinary descriptive
/// summaries (which report volume and trend, not advice) are not dropped.
/// </summary>
public static class NarrativeGuard
{
    // EVERY marker MUST be lowercase (only the input is lowercased). Chosen to catch
    // the DIRECTIVE shapes an injection produces, not soft descriptive modals.
    private static readonly string[] ActionMarkers =
    {
        // Finnish — recommendation / proposal / demand. First-person forms only:
        // the directive is the MODEL advising, whereas 3rd-person "asiakas suosittelisi
        // kauppaa" is descriptive praise, not an instruction.
        "suosittelen", "suosittelemme", "suosittelisin", "ehdotan", "ehdotamme",
        "vaadin", "vaadimme",
        // Finnish — personnel / closure / compensation directives (incl. imperatives)
        "on erotettava", "tulee erottaa", "erota ", "erottakaa", "irtisano",
        "irtisanokaa", "palkatkaa", "sulkekaa", "sulje osasto", "suljettava",
        "antakaa hyvitys", "hyvittäkää", "korvatkaa asiakkaille", "ryhtykää toimiin",
        // English — recommendation / directive / personnel / closure / refund
        "we recommend", "i recommend", "should fire", "should be fired", "must fire",
        "recommend firing", "recommend closing", "should close", "should refund",
        "issue a refund", "take disciplinary", "take immediate action",
    };

    public static bool LooksActionBearing(string? narrative)
    {
        if (string.IsNullOrEmpty(narrative))
            return false;
        var lower = narrative!.ToLowerInvariant();
        foreach (var marker in ActionMarkers)
            if (lower.Contains(marker, StringComparison.Ordinal))
                return true;
        return false;
    }
}
