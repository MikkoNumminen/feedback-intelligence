namespace RetailFeedback.Domain.Alerts;

public sealed record AlertHit(string Category, string Pattern);

/// <summary>
/// The deterministic alert layer's core: case-insensitive invariant substring
/// over the RAW feedback text; patterns are Finnish stems (the exact contract
/// documented in config/alert-keywords.json). Runs FIRST and independent of
/// any LLM — cheap, never sleeps, never hallucinates. The LLM layer may ADD
/// alerts but can never remove a deterministic one.
/// </summary>
public static class AlertMatcher
{
    public static List<AlertHit> Match(string text, IReadOnlyDictionary<string, IReadOnlyList<string>> categories)
    {
        var hits = new List<AlertHit>();
        foreach (var (category, patterns) in categories)
            foreach (var pattern in patterns)
                if (text.Contains(pattern, StringComparison.InvariantCultureIgnoreCase))
                    hits.Add(new AlertHit(category, pattern));
        return hits;
    }
}
