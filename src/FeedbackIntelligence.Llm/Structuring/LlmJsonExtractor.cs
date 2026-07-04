using System.Text.Json;
using System.Text.RegularExpressions;

namespace FeedbackIntelligence.Llm.Structuring;

/// <summary>
/// Extracts a single JSON object from raw LLM output. <c>strict</c> reports
/// whether the trimmed output already WAS that object — the discipline signal
/// the eval measures. The salvage path (think-trace strip, fence strip,
/// outermost brace span) is what production tolerates; distance from strict is
/// itself a measurement.
/// </summary>
public static partial class LlmJsonExtractor
{
    public static bool TryExtractObject(string raw, out JsonDocument? doc, out bool strict)
    {
        if (TryParseObject(raw.Trim(), out doc))
        {
            strict = true;
            return true;
        }

        strict = false;
        var salvaged = Salvage(raw);
        return salvaged is not null && TryParseObject(salvaged, out doc);
    }

    private static bool TryParseObject(string candidate, out JsonDocument? doc)
    {
        doc = null;
        if (candidate.Length == 0)
            return false;
        try
        {
            var parsed = JsonDocument.Parse(candidate);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                parsed.Dispose();
                return false;
            }
            doc = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? Salvage(string raw)
    {
        var s = ThinkBlock().Replace(raw, "").Trim();
        var fence = FencedBlock().Match(s);
        if (fence.Success)
            s = fence.Groups[1].Value.Trim();
        var first = s.IndexOf('{');
        var last = s.LastIndexOf('}');
        return first >= 0 && last > first ? s[first..(last + 1)] : null;
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"```(?:json)?\s*(.*?)```", RegexOptions.Singleline)]
    private static partial Regex FencedBlock();
}
