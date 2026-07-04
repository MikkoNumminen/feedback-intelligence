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
            ".alert{border-left:4px solid #b3261e}.muted{color:#66707d;font-size:.85rem}.ids{font-size:.75rem;color:#66707d;word-break:break-all}</style></head><body>");
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
                $"<div class=\"muted\">{E(alert.Source)} · {E(alert.Timestamp)} · {origin}</div>" +
                $"<div class=\"ids\">{E(alert.FeedbackId)}</div></div>");
        }

        sb.AppendLine($"<h2>{E(t.ThemesHeading)} ({report.Themes.Count})</h2>");
        foreach (var theme in report.Themes)
        {
            sb.AppendLine($"<div class=\"card\"><strong>{E(theme.Title)}</strong>" +
                $"<div class=\"muted\">{E(theme.Category)} · {theme.Count} {E(t.ItemsWord)} · {E(t.TrendWord)}: {E(theme.DirectionLabel)}</div>" +
                $"<p>{E(theme.Narrative)}</p>" +
                $"<div class=\"ids\">{E(string.Join(" ", theme.FeedbackIds))}</div></div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}
