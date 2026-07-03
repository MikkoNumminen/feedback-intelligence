using System.Globalization;

namespace RetailFeedback.Api;

/// <summary>
/// Timestamps are stored and compared as strings (SQLite), so every stored or
/// queried instant is normalized to one fixed-width UTC round-trip format —
/// mixed client offsets ('Z' vs '+03:00') would otherwise compare wrongly
/// under lexical ordering. Offset-less inputs are assumed UTC.
/// </summary>
public static class TimestampNormalizer
{
    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = "";
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return false;
        normalized = parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        return true;
    }
}
