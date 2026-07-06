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
    public static string Render(ManagementReport report, string language)
    {
        var t = ReportText.Snapshot(language);
        var sb = new StringBuilder();
        sb.AppendLine($"<!DOCTYPE html><html lang=\"{t.HtmlLang}\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>{E(t.PageTitle)}</title>");
        sb.AppendLine("<style>body{font:16px/1.5 system-ui,sans-serif;max-width:52rem;margin:0 auto;padding:1rem;color:#1c2430}" +
            "h1{font-size:1.3rem}.badge{display:inline-block;background:#b3541e;color:#fff;border-radius:.4rem;padding:.15rem .5rem;font-size:.8rem}" +
            ".card{border:1px solid #d9dde3;border-radius:.6rem;padding:.9rem;margin:.7rem 0}" +
            ".alert{border-left:4px solid #b3261e}.muted{color:#66707d;font-size:.85rem}" +
            "details{margin-top:.5rem}summary{cursor:pointer;color:#0b5fa5;font-size:.85rem}" +
            ".src{border-top:1px solid #eef1f4;padding:.5rem 0;font-size:.92rem;white-space:pre-wrap}</style></head><body>");
        sb.AppendLine($"<h1>{E(t.Heading)} <span class=\"badge\">{E(t.SavedBadge)}</span></h1>");
        sb.AppendLine($"<p class=\"muted\">{E(t.Window)} {E(report.WindowFrom)} – {E(report.WindowTo)} · " +
            $"{report.TotalItems} {E(t.Items)} · {E(t.Generated)} {E(report.GeneratedAt)}</p>");

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
            sb.Append($"<p>{E(theme.Narrative)}</p>");
            if (theme.Sources.Count > 0)
            {
                sb.Append($"<details><summary>{theme.Sources.Count} {E(t.ItemsWord)}</summary>");
                foreach (var s in theme.Sources)
                    sb.Append($"<div class=\"src\">{E(s.Text)}<div class=\"muted\">{E(s.Source)} · {E(s.Timestamp)} · {E(s.Severity)}</div></div>");
                sb.Append("</details>");
            }
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
