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

    /// <summary>Human name of the category field in the active domain's language.</summary>
    public required string CategoryFieldLabel { get; init; }

    public required IReadOnlySet<string> Categories { get; init; }
    public required IReadOnlySet<string> Severities { get; init; }
    public required IReadOnlySet<string> Types { get; init; }

    public required IReadOnlyDictionary<string, string> CategoryLabels { get; init; }
    public required IReadOnlyDictionary<string, string> SeverityLabels { get; init; }
    public required IReadOnlyDictionary<string, string> TypeLabels { get; init; }

    /// <summary>The domain's ingest channels (the accepted `source` values), in
    /// declared order — retail's google_review/email/web_form/desk, a game
    /// studio's steam_review/support_ticket/discord/in_game. Channels are
    /// domain-specific, so the ingest layer accepts exactly these; the generator
    /// draws a channel from this list when a noise item declares none.</summary>
    public required IReadOnlyList<string> Sources { get; init; }
}
