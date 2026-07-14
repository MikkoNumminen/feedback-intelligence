namespace FeedbackIntelligence.Api.Analysis;

/// <summary>
/// Language-specific user-facing report strings. The active domain's Language
/// (domain.json "language", default "en") selects the set; an unknown language
/// falls back to English. Retail is "fi" (its only audience); game and any new
/// domain default to "en". Direction is stored as a neutral KEY
/// (stable/growing/declining/worsening) so the verify gate and JSON stay
/// language-independent; the localized label is presentation only.
/// </summary>
internal static class ReportText
{
    private static readonly Dictionary<string, Dictionary<string, string>> DirectionLabels =
        new(StringComparer.Ordinal)
        {
            ["fi"] = new(StringComparer.Ordinal)
                { ["stable"] = "vakaa", ["growing"] = "kasvava", ["declining"] = "laskeva", ["worsening"] = "paheneva" },
            ["en"] = new(StringComparer.Ordinal)
                { ["stable"] = "stable", ["growing"] = "growing", ["declining"] = "declining", ["worsening"] = "worsening" },
        };

    public static string DirectionLabel(string directionKey, string language)
    {
        var set = DirectionLabels.TryGetValue(language, out var s) ? s : DirectionLabels["en"];
        return set.TryGetValue(directionKey, out var label) ? label : directionKey;
    }

    /// <summary>The deterministic fallback narrative shown when the LLM narrative
    /// is unavailable/over-budget.</summary>
    public static string FallbackNarrative(int count, string topThemes, string directionLabel, string language) =>
        language == "fi"
            ? $"{count} palautetta aikavälillä. Yleisimmät aiheet: {topThemes}. Suunta: {directionLabel}. " +
              "(Automaattinen kooste — kielimallin tiivistelmä ei ollut käytettävissä.)"
            : $"{count} feedback item(s) in the window. Top themes: {topThemes}. Trend: {directionLabel}. " +
              "(Automated summary — the language-model narrative was unavailable.)";

    /// <summary>The whole-window scope word fed to the synthesis data block as the
    /// category slot of the live summary's Overall narrative (ADR-0026). Lives here
    /// with the other localized strings so a new language cannot miss it.</summary>
    public static string WholeWindowScope(string language) =>
        language == "fi" ? "kaikki" : "all";

    /// <summary>Generic reason shown for an LLM-screened safety alert when the
    /// nomination pass returns no specific one — the per-item yes/no screen has
    /// already decided it IS an alert, so the alert still shows.</summary>
    public static string PossibleSafetyAlert(string language) =>
        language == "fi"
            ? "Malli tunnisti mahdollisen turvallisuusriskin — tarkista palaute."
            : "The model flagged this as a possible safety risk — review the item.";

    /// <summary>Row labels for the digest fed to the synthesis model (not shown to
    /// the user, but kept in the domain's language so the model's input is coherent).</summary>
    public static SynthesisLabels Synthesis(string language) => language == "fi" ? SynthFi : SynthEn;

    public sealed record SynthesisLabels(string Count, string Trend, string Severities, string Themes, string Excerpts);

    private static readonly SynthesisLabels SynthFi =
        new("palautteita", "suunta", "vakavuudet", "teemat", "poimintoja");
    private static readonly SynthesisLabels SynthEn =
        new("feedback items", "trend", "severities", "themes", "excerpts");

    /// <summary>Chrome for the self-contained snapshot page.</summary>
    public static SnapshotLabels Snapshot(string language) => language == "fi" ? SnapFi : SnapEn;

    public sealed record SnapshotLabels(
        string HtmlLang, string PageTitle, string SavedBadge, string Heading,
        string Window, string Items, string Generated,
        string AlertsHeading, string NoAlerts, string KeywordOrigin, string ModelOrigin,
        string ThemesHeading, string ItemsWord, string TrendWord,
        // A2 report-surface visibility on the snapshot (shared-link fallback):
        // Flagged = per-theme warning, FlaggedItem = per-message tag.
        string Flagged, string FlaggedItem,
        // ADR-0033: heading for the collapsed non-substantive (rasismi/asiaton) section.
        string ModerationHeading);

    private static readonly SnapshotLabels SnapFi = new(
        "fi", "Palautetilanne — tallennettu tilannekuva", "Tallennettu tilannekuva", "Palautetilanne",
        "Aikaväli", "palautetta", "koostettu",
        "Hälytykset", "Ei hälytyksiä aikavälillä.", "sanahaku", "kielimalli",
        "Teemat ja trendit", "palautetta", "suunta",
        "tarkistettavana (mahdollinen manipulointi — mukana laskennassa)", "tarkistettava",
        "Moderoitava sisältö");

    private static readonly SnapshotLabels SnapEn = new(
        "en", "Feedback situation — saved snapshot", "Saved snapshot", "Feedback situation",
        "Window", "items", "generated",
        "Alerts", "No alerts in the window.", "keyword", "model",
        "Themes & trends", "items", "trend",
        "to review (possible manipulation — still counted)", "review",
        "Content to moderate");
}
