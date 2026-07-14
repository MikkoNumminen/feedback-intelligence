namespace FeedbackIntelligence.Core.Domain;

/// <summary>
/// The taxonomy a domain supplies to the neutral core: the valid category /
/// severity / type value sets, their display labels, and the human name of the
/// category field ("osasto" for retail, "area"/"platform" for a game studio).
/// The core reads this instead of any hardcoded values. Loaded from
/// domains/&lt;active&gt;/domain.json — see <see cref="ActiveDomain"/>.
/// </summary>
public sealed class DomainDescriptor
{
    public required string Name { get; init; }

    /// <summary>The domain's output/UI language (short code, e.g. "fi", "en").
    /// User-facing report prose, direction labels, and the frontends follow it.
    /// The core default is "en"; retail overrides to "fi" (its only audience).</summary>
    public required string Language { get; init; }

    /// <summary>Human name of the category field in the active domain's language.</summary>
    public required string CategoryFieldLabel { get; init; }

    public required IReadOnlySet<string> Categories { get; init; }
    public required IReadOnlySet<string> Severities { get; init; }
    public required IReadOnlySet<string> Types { get; init; }

    public required IReadOnlyDictionary<string, string> CategoryLabels { get; init; }
    public required IReadOnlyDictionary<string, string> SeverityLabels { get; init; }
    public required IReadOnlyDictionary<string, string> TypeLabels { get; init; }

    /// <summary>Sentiment (polarity) value set → display label, e.g.
    /// positive/negative/neutral (retail relabels to Finnish). Core-defaulted, so
    /// always present. See <see cref="TypeSentiment"/> for how an item's sentiment
    /// is derived.</summary>
    public IReadOnlyDictionary<string, string> SentimentLabels { get; init; } =
        new Dictionary<string, string>(CoreDefaults.Sentiments, StringComparer.Ordinal);

    /// <summary>The sentiment value set (keys of <see cref="SentimentLabels"/>) —
    /// the legal values for the optional model-authored sentiment field (ADR-0031),
    /// parallel to <see cref="Severities"/>/<see cref="Types"/>.</summary>
    public IReadOnlySet<string> Sentiments { get; init; } =
        CoreDefaults.Sentiments.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>Deterministic map from a feedback <c>type</c> key to a sentiment
    /// key (ADR-0030). The report derives each item's positive/negative/neutral
    /// indicator from this until a model-authored sentiment field replaces it.
    /// Keys are a subset of <see cref="Types"/>; values are keys of
    /// <see cref="SentimentLabels"/>. Both are enforced at load.</summary>
    public IReadOnlyDictionary<string, string> TypeSentiment { get; init; } =
        new Dictionary<string, string>(CoreDefaults.TypeSentiment, StringComparer.Ordinal);

    /// <summary>The sentiment key for a feedback <c>type</c>, or null if the type
    /// carries no polarity signal (not in <see cref="TypeSentiment"/>). This is the
    /// single seam the future model-authored sentiment field will swap behind
    /// (ADR-0030): callers ask the domain, not the type map directly.</summary>
    public string? SentimentOf(string type) =>
        TypeSentiment.TryGetValue(type, out var s) ? s : null;

    /// <summary>The domain's ingest channels (the accepted `source` values), in
    /// declared order — retail's google_review/email/web_form/desk, a game
    /// studio's steam_review/support_ticket/discord/in_game. Channels are
    /// domain-specific, so the ingest layer accepts exactly these; the generator
    /// draws a channel from this list when a noise item declares none.</summary>
    public required IReadOnlyList<string> Sources { get; init; }

    /// <summary>Optional per-category guidance shown ONLY to the structuring
    /// model (appended to the label inside the prompt's category list). Lets a
    /// non-obvious category (e.g. retail's "asiaton") carry an explanation
    /// without bloating the short display label UIs render. Empty by default;
    /// keys are validated against <see cref="Categories"/> at load.</summary>
    public IReadOnlyDictionary<string, string> CategoryHints { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Optional key of the domain's catch-all category (retail's
    /// "muu") — the department that absorbs feedback with no better home.
    /// Operator maintenance re-runs structuring over items sitting here when the
    /// vocabulary changes (a new category may now fit). ADR-0035 retired the
    /// live-summary emergent-topic split, so it renders as one plain category.
    /// Null = the domain has no catch-all. Validated against
    /// <see cref="Categories"/> at load.</summary>
    public string? CatchAllCategory { get; init; }

    /// <summary>Optional categories views should sort LAST regardless of count
    /// (retail demotes "asiaton": hostile content must not lead the page).
    /// Presentation-only — counts, trends and alerts are unaffected.
    /// Validated against <see cref="Categories"/> at load.</summary>
    public IReadOnlyList<string> DemotedCategories { get; init; } = [];
}
