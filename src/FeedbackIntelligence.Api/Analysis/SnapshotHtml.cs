using System.Net;
using System.Text;

namespace FeedbackIntelligence.Api.Analysis;

/// <summary>
/// Renders the persisted snapshot as one self-contained, JS-free HTML page in the
/// active domain's language — what a shared link shows when the backend is
/// unreachable. All values are HTML-encoded; feedback IDs are plain text here (no
/// live click-through without a backend).
/// </summary>
public static class SnapshotHtml
{
    public static string Render(
        ManagementReport report, string language,
        IReadOnlyDictionary<string, string>? sentimentLabels = null)
    {
        var t = ReportText.Snapshot(language);
        var labels = sentimentLabels ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.AppendLine($"<!DOCTYPE html><html lang=\"{t.HtmlLang}\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>{E(t.PageTitle)}</title>");
        sb.AppendLine("<style>body{font:16px/1.5 system-ui,sans-serif;max-width:52rem;margin:0 auto;padding:1rem;color:#1c2430}" +
            "h1{font-size:1.3rem}.badge{display:inline-block;background:#b3541e;color:#fff;border-radius:.4rem;padding:.15rem .5rem;font-size:.8rem}" +
            ".card{border:1px solid #d9dde3;border-radius:.6rem;padding:.9rem;margin:.7rem 0}" +
            ".alert{border-left:4px solid #b3261e}.muted{color:#66707d;font-size:.85rem}" +
            "details{margin-top:.5rem}summary{cursor:pointer;color:#0b5fa5;font-size:.85rem}" +
            ".src{border-top:1px solid #eef1f4;padding:.5rem 0;font-size:.92rem;white-space:pre-wrap}" +
            ".flag{color:#8a5a00;font-weight:600;font-size:.85rem}" +
            ".snt{display:inline-block;border-radius:.4rem;padding:.02rem .4rem;font-size:.8rem;margin-right:.3rem}" +
            ".snt-positive{background:#e6f4ea;color:#1e7e34}.snt-negative{background:#fdecea;color:#b3261e}" +
            ".snt-neutral{background:#eef1f4;color:#66707d}</style></head><body>");
        sb.AppendLine($"<h1>{E(t.Heading)} <span class=\"badge\">{E(t.SavedBadge)}</span></h1>");
        sb.AppendLine($"<p class=\"muted\">{E(t.Window)} {E(report.WindowFrom)} – {E(report.WindowTo)} · " +
            $"{report.TotalItems} {E(t.Items)} · {E(t.Generated)} {E(report.GeneratedAt)}</p>");
        // Whole-window sentiment (polarity) mix (ADR-0030).
        var overallPills = SentPills(report.SentimentCounts, labels);
        if (overallPills.Length > 0)
            sb.AppendLine($"<p>{overallPills}</p>");

        sb.AppendLine($"<h2>{E(t.AlertsHeading)} ({report.Alerts.Count})</h2>");
        if (report.Alerts.Count == 0)
            sb.AppendLine($"<p class=\"muted\">{E(t.NoAlerts)}</p>");
        foreach (var alert in report.Alerts)
        {
            var origin = alert.LlmReason is null
                ? t.KeywordOrigin + ": " + E(string.Join(", ", alert.DeterministicHits.Select(h => h.Pattern)))
                : t.ModelOrigin + ": " + E(alert.LlmReason);
            sb.AppendLine($"<div class=\"card alert\"><strong>{E(alert.TextExcerpt)}</strong>" +
                $"<div class=\"muted\">{E(alert.Source)} · {E(alert.Timestamp)} · {origin}</div></div>");
        }

        sb.AppendLine($"<h2>{E(t.ThemesHeading)} ({report.Themes.Count})</h2>");
        foreach (var theme in report.Themes)
        {
            sb.Append($"<div class=\"card\"><strong>{E(theme.Title)}</strong>");
            sb.Append($"<div class=\"muted\">{E(theme.Category)} · {theme.Count} {E(t.ItemsWord)} · {E(t.TrendWord)}: {E(theme.DirectionLabel)}</div>");
            var themePills = SentPills(theme.SentimentCounts, labels);
            if (themePills.Length > 0)
                sb.Append($"<div>{themePills}</div>");
            // A2: a flagged (possibly manipulated) item stays counted but its presence
            // is surfaced here too, so the shared-link snapshot is not silent.
            if (theme.FlaggedCount > 0)
                sb.Append($"<div class=\"flag\">⚠ {theme.FlaggedCount} {E(t.Flagged)}</div>");
            sb.Append($"<p>{E(theme.Narrative)}</p>");
            if (theme.Sources.Count > 0)
            {
                sb.Append($"<details><summary>{theme.Sources.Count} {E(t.ItemsWord)}</summary>");
                foreach (var s in theme.Sources)
                {
                    // Unrated (demoted) themes show no severity or sentiment — the
                    // category is the signal (ADR-0032). Sentiment is already null.
                    var sev = theme.Unrated ? "" : $" · {E(s.Severity)}";
                    sb.Append($"<div class=\"src\">{E(s.Text)}<div class=\"muted\">{E(s.Source)} · {E(s.Timestamp)}{sev}{SentBadge(s.Sentiment, labels)}" +
                        (s.NeedsReview ? $" · <span class=\"flag\">⚠ {E(t.FlaggedItem)}</span>" : "") + "</div></div>");
                }
                sb.Append("</details>");
            }
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>Sentiment (polarity) mix as colored pills (ADR-0030), in the
    /// domain's declared label order; empty string when there is nothing to show.</summary>
    private static string SentPills(IReadOnlyDictionary<string, int>? counts, IReadOnlyDictionary<string, string> labels)
    {
        if (counts is null || counts.Count == 0)
            return "";
        var sb = new StringBuilder();
        foreach (var key in labels.Keys.Concat(counts.Keys).Distinct(StringComparer.Ordinal))
            if (counts.TryGetValue(key, out var n) && n > 0)
                sb.Append($"<span class=\"snt snt-{E(key)}\">{E(labels.GetValueOrDefault(key, key))} {n}</span>");
        return sb.ToString();
    }

    /// <summary>A single sentiment pill for one item, prefixed with a separator;
    /// empty when the item carries no sentiment.</summary>
    private static string SentBadge(string? key, IReadOnlyDictionary<string, string> labels) =>
        string.IsNullOrEmpty(key)
            ? ""
            : $" · <span class=\"snt snt-{E(key)}\">{E(labels.GetValueOrDefault(key, key))}</span>";

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
