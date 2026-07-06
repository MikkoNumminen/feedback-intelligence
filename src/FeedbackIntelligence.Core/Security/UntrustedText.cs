using System.Globalization;

namespace FeedbackIntelligence.Core.Security;

/// <summary>
/// Neutral prompt-injection hygiene for untrusted feedback text. This is the single
/// Core chokepoint every domain inherits — untrusted text must pass through here
/// before it is spliced into ANY model prompt (structuring, synthesis, alert
/// screen). It is defense-in-depth, NOT a proof of safety: prompt injection is an
/// unsolved problem and a determined adversary against an 8B local model is not
/// stopped by delimiting. What it removes is the concrete breakout vectors the A0
/// audit found (see ADR-0021):
///   - the closing delimiter cannot be forged (it is stripped from the content,
///     to a fixpoint so nested/split copies cannot reassemble into a live marker),
///   - the text cannot break out of an inline `"..."` quote (quotes neutralized),
///   - the text cannot forge a new prompt line or a `- [id] "..."` list row
///     (every line/row-forming character is collapsed to a space).
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
    /// a list row): strip the fence markers, collapse every line/row-forming
    /// character to a single space so it cannot forge a line or a `- [id] "..."`
    /// row, and turn double quotes and backticks into apostrophes so it cannot
    /// break out of a `"..."` context.</summary>
    public static string Neutralize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Strip the fence markers to a FIXPOINT. String.Replace makes a single
        // left-to-right pass and never re-scans its own output, so a marker split
        // around an inner copy of itself — e.g. "<<<PALAU<<<PALAUTE_LOPPU>>>TE_LOPPU>>>"
        // — would otherwise reassemble into a LIVE close marker after one pass and
        // forge the fence boundary (A0/ADR-0021). Loop until nothing more is removed;
        // each pass strictly shrinks the string, so this terminates.
        var stripped = text!;
        string previous;
        do
        {
            previous = stripped;
            stripped = previous
                .Replace(Open, "", StringComparison.OrdinalIgnoreCase)
                .Replace(Close, "", StringComparison.OrdinalIgnoreCase);
        } while (stripped.Length != previous.Length);

        var sb = new System.Text.StringBuilder(stripped.Length);
        foreach (var ch in stripped)
        {
            sb.Append(ch switch
            {
                '"' or '`' => '\'',
                // Anything that could start a new visual line becomes a space, so the
                // text cannot forge a prompt line or a `- [id] "..."` row: every C0/C1
                // control char (CR, LF, TAB, vertical tab, form feed, NEL U+0085, ...),
                _ when char.IsControl(ch) => ' ',
                // ...plus the Unicode line/paragraph separators U+2028/U+2029, which are
                // separators (Zl/Zp), not control chars, but a model may still treat as
                // a line break. Matched by category so no raw separator is in source.
                _ when char.GetUnicodeCategory(ch)
                    is UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator => ' ',
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
