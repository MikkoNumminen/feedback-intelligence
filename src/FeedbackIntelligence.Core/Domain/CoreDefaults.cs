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
}
