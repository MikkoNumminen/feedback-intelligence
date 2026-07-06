namespace FeedbackIntelligence.Core.Security;

/// <summary>
/// Deterministic scan of untrusted feedback text for prompt-injection SYMPTOMS --
/// text that addresses the model as an instructor instead of describing an
/// experience. It does NOT decide an item is malicious or drop it: it raises a
/// FLAG so the item is preserved, marked needs_review, and surfaced (ADR-0021 A2).
/// Complements the A1 chokepoint -- A1 stops the text from breaking OUT of its
/// data block; this catches an in-band imperative that stays INSIDE the block and
/// would otherwise silently shape the classification.
///
/// Language-neutral core: patterns cover the Finnish corpus and English (the game
/// domain / mixed input). Matching is lowercase-invariant substring -- the same
/// cheap, never-hallucinates contract as the deterministic alert layer. A false
/// positive costs a human glance, not a dropped item, so the patterns are chosen
/// specific/multi-word to keep benign feedback (including the no-keyword safety
/// story, which describes structural failure, not instructions) clear.
/// </summary>
public static class InjectionSignals
{
    // (symptom category, lowercase phrase). Category is what surfaces in review and
    // telemetry, so several phrases fold into one category. Phrases are deliberately
    // specific to avoid firing on ordinary words ("contact", "the system broke").
    private static readonly (string Category, string Phrase)[] Patterns =
    {
        // Override / ignore-the-system
        ("override", "ignore previous"),
        ("override", "ignore above"),
        ("override", "ignore all previous"),
        ("override", "disregard previous"),
        ("override", "disregard the above"),
        ("override", "forget previous instructions"),
        ("override", "ohita edelliset"),
        ("override", "ohita aiemmat ohjeet"),
        ("override", "unohda edelliset"),
        ("override", "unohda aiemmat ohjeet"),
        ("override", "älä välitä ohjeista"),
        // New task / real instructions
        ("new-instructions", "new instructions"),
        ("new-instructions", "your real task"),
        ("new-instructions", "uusi ohje"),
        ("new-instructions", "uudet ohjeet"),
        ("new-instructions", "todellinen tehtäväsi"),
        // Role / system override
        ("role-override", "system prompt"),
        ("role-override", "you are now"),
        ("role-override", "assistant:"),
        ("role-override", "developer mode"),
        ("role-override", "olet nyt tekoäly"),
        ("role-override", "järjestelmäkehote"),
        // Field / classification injection
        ("field-injection", "set severity"),
        ("field-injection", "severity:"),
        ("field-injection", "severity ="),
        ("field-injection", "mark as critical"),
        ("field-injection", "classify this as"),
        ("field-injection", "aseta severity"),
        ("field-injection", "vakavuus:"),
        ("field-injection", "merkitse kriittiseksi"),
        ("field-injection", "luokittele tämä"),
        // Forced answer / format forge
        ("format-forge", "```json"),
        ("format-forge", "\"role\""),
        ("format-forge", "[inst]"),
        ("format-forge", "<|"),
        ("format-forge", "vastaus: kyllä"),
        ("format-forge", "vastaus:kyllä"),
        ("format-forge", "output only"),
    };

    /// <summary>Flag added by the ingest layer when a symptom co-occurs with a
    /// model-assigned SEVERE rating -- the "talked-into-critical" case. Kept here so
    /// the label is defined next to the patterns.</summary>
    public const string SevereRatingFlag = "severe-rating-with-injection-symptoms";

    /// <summary>Severities the model may have been talked into. Matches the
    /// structuring schema's severe tiers; a domain with other severity names simply
    /// never adds the co-occurrence flag (safe degradation, no false alarm).</summary>
    public static readonly IReadOnlySet<string> SevereSeverities =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "high", "critical" };

    /// <summary>Return the DISTINCT symptom categories found in the text, in a
    /// stable (first-seen) order. Empty list = clean.</summary>
    public static IReadOnlyList<string> Detect(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var lower = text!.ToLowerInvariant();
        var found = new List<string>();
        foreach (var (category, phrase) in Patterns)
            if (!found.Contains(category) && lower.Contains(phrase, StringComparison.Ordinal))
                found.Add(category);
        return found;
    }

    /// <summary>True when the text shows any injection symptom.</summary>
    public static bool IsSuspicious(string? text) => Detect(text).Count > 0;
}
