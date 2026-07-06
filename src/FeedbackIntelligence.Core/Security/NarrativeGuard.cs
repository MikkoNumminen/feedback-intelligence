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
    // EVERY marker MUST be lowercase (only the input is lowercased). FIRST-PERSON /
    // IMPERATIVE forms only: the directive is the MODEL advising or commanding, never
    // a 3rd-person DESCRIPTION of what customers said. This is the key to low false
    // positives — Finnish "erottaa" is also "to separate/differ", "irtisanoa" is the
    // standard verb for cancelling a subscription, and English "should fire/close" is
    // both a personnel directive and weapon-fire / a game timer / a prompt-compliant
    // "players say X should close" observation. Substring matching can't tell those
    // apart, so the ambiguous 3rd-person/bare-modal forms are deliberately excluded
    // (PR-#25 review). The prompt is the primary defense; this is a narrow backstop.
    private static readonly string[] ActionMarkers =
    {
        // Finnish — the model recommending / proposing / demanding (first person only;
        // 3rd-person "asiakas suosittelisi", "asiakkaat vaativat" is description).
        "suosittelen", "suosittelemme", "suosittelisin", "suosittelisimme",
        "ehdotan", "ehdotamme", "vaadin", "vaadimme",
        // Finnish — unambiguous directive imperatives (2nd-person commands to act).
        "irtisanokaa", "sulkekaa", "sulje osasto", "hyvittäkää", "antakaa hyvitys",
        "korvatkaa asiakkaille", "ryhtykää toimiin",
        // English — the model recommending / directing (first person only; a
        // prompt-compliant "players say the mode should close" stays a description).
        "we recommend", "i recommend", "we suggest", "i suggest",
        "we should", "we must", "we need to",
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
