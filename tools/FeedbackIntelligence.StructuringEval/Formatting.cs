namespace FeedbackIntelligence.StructuringEval;

/// <summary>Small shared text helpers for the eval tool's console + markdown output,
/// so the identical one-liners don't get copied per file (they were in three).</summary>
internal static class Formatting
{
    /// <summary>Ellipsize to at most <paramref name="max"/> chars.</summary>
    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
