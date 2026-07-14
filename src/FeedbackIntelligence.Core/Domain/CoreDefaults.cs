namespace FeedbackIntelligence.Core.Domain;

/// <summary>
/// Core-universal defaults for the generic feedback dimensions. A domain MAY
/// override severities/types (they are domain-overridable), but if it omits
/// them these apply — so every domain gets sane defaults for free.
/// </summary>
public static class CoreDefaults
{
    public static readonly IReadOnlyDictionary<string, string> Severities =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["low"] = "low", ["medium"] = "medium", ["high"] = "high", ["critical"] = "critical",
        };

    public static readonly IReadOnlyDictionary<string, string> Types =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["complaint"] = "complaint", ["praise"] = "praise", ["suggestion"] = "suggestion",
            ["question"] = "question", ["other"] = "other",
        };

    /// <summary>The sentiment (polarity) value set + display labels. A domain MAY
    /// override to relabel (retail → Finnish) or extend, but if it omits them these
    /// three apply. Kept minimal on purpose — a positive/negative indicator with an
    /// explicit neutral, not a fine-grained scale.</summary>
    public static readonly IReadOnlyDictionary<string, string> Sentiments =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["positive"] = "positive", ["negative"] = "negative", ["neutral"] = "neutral",
        };

    /// <summary>Default deterministic map from a feedback <c>type</c> to its
    /// sentiment: praise reads positive, a complaint negative, everything else
    /// neutral (a question or a constructive suggestion is not a polarity signal).
    /// A domain MAY override; if it omits <c>typeSentiment</c>, the entries whose
    /// type the domain actually declares apply. This is the source of the
    /// deterministic indicator until a model-authored sentiment field lands
    /// (ADR-0030).</summary>
    public static readonly IReadOnlyDictionary<string, string> TypeSentiment =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["complaint"] = "negative", ["praise"] = "positive", ["suggestion"] = "neutral",
            ["question"] = "neutral", ["other"] = "neutral",
        };
}
