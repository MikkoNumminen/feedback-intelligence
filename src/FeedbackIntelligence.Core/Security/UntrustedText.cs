namespace FeedbackIntelligence.Core.Security;

/// <summary>
/// Neutral prompt-injection hygiene for untrusted feedback text. This is the single
/// Core chokepoint every domain inherits — untrusted text must pass through here
/// before it is spliced into ANY model prompt (structuring, synthesis, alert
/// screen). It is defense-in-depth, NOT a proof of safety: prompt injection is an
/// unsolved problem and a determined adversary against an 8B local model is not
/// stopped by delimiting. What it removes is the concrete breakout vectors the A0
/// audit found (see ADR-0021):
///   - the closing delimiter cannot be forged (it is stripped from the content),
///   - the text cannot break out of an inline `"..."` quote (quotes neutralized),
///   - the text cannot forge a new prompt line or a `- [id] "..."` list row
///     (newlines/CR/tab collapsed to spaces).
/// It does NOT stop an in-band imperative that stays within the data block from
/// influencing the model — that residual is handled by output validation
/// (A2 salvage guard, A3 constrained synthesis) and named honestly in the docs.
/// </summary>
public static class UntrustedText
{
    /// <summary>Delimiters marking a block of customer data. Chosen to be
    /// implausible in real feedback; stripped from content so they cannot be forged.</summary>
    public const string Open = "<<<ASIAKASPALAUTE>>>";
    public const string Close = "<<<PALAUTE_LOPPU>>>";

    /// <summary>Make untrusted text safe to splice INLINE (inside a quoted field or
    /// a list row): strip the fence markers, collapse CR/LF/TAB to single spaces so
    /// it cannot forge a line or row, and turn double quotes and backticks into
    /// apostrophes so it cannot break out of a `"..."` context.</summary>
    public static string Neutralize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var sb = new System.Text.StringBuilder(text!.Length);
        foreach (var ch in text!.Replace(Open, "", System.StringComparison.OrdinalIgnoreCase)
                                 .Replace(Close, "", System.StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(ch switch
            {
                '\r' or '\n' or '\t' => ' ',
                '"' or '`' => '\'',
                _ => ch,
            });
        }
        return sb.ToString();
    }

    /// <summary>Wrap untrusted text as a clearly-delimited data BLOCK (for the
    /// structuring prompt). The content is Neutralized first, so it cannot forge the
    /// closing delimiter or inject structure.</summary>
    public static string Fence(string? text) => $"{Open}\n{Neutralize(text)}\n{Close}";
}
