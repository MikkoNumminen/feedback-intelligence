using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RetailFeedback.Api.Storage;
using RetailFeedback.Llm;
using RetailFeedback.Llm.Structuring;

namespace RetailFeedback.Api.Analysis;

/// <summary>
/// Two-layer analysis. Layer 1 is deterministic and always succeeds: window
/// query, alert collection, department grouping, counts, trend direction.
/// Layer 2 is the LLM: Finnish narratives per group and alert nominations over
/// keyword-less items. Every LLM claim must cite provided feedback IDs; a
/// narrative whose citations fail validation is DROPPED to a deterministic
/// fallback and the drop is logged — the view never shows an ungrounded claim.
/// The report generates even with the LLM entirely down.
/// </summary>
public sealed class ReportService(
    FeedbackStore store,
    [FromKeyedServices(LlmServiceCollectionExtensions.SynthesisKey)] IChatClient synthesisClient,
    LlmGate llmGate,
    IOptions<ReportOptions> options,
    ILogger<ReportService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<ManagementReport> GenerateAsync(string fromIso, string toIso, CancellationToken ct)
    {
        var opts = options.Value;
        var items = await store.QueryAsync(fromIso, toIso, opts.MaxItemsPerWindow, ct);
        var dropped = 0;

        // --- Layer 1: deterministic alerts, always present ---
        var alerts = items
            .Where(i => i.Alerts.Count > 0)
            .OrderByDescending(i => i.Timestamp, StringComparer.Ordinal)
            .Select(i => new ReportAlert(i.Id, i.Source, i.Timestamp, Excerpt(i.Text), i.Alerts, null))
            .ToList();

        // --- Layer 2a: LLM alert nominations over keyword-less items (may ADD, never removes) ---
        if (opts.AlertNominationEnabled)
        {
            var candidates = items.Where(i => i.Alerts.Count == 0).ToList();
            foreach (var batch in candidates.Chunk(opts.MaxItemsPerLlmCall))
            {
                var nominated = await NominateAlertsAsync(batch, ct);
                foreach (var (id, reason) in nominated)
                {
                    var item = batch.FirstOrDefault(i => i.Id == id);
                    if (item is null)
                    {
                        dropped++;
                        logger.LogWarning("Dropped LLM alert nomination for unknown id '{Id}' — not in the provided batch.", id);
                        continue;
                    }
                    alerts.Add(new ReportAlert(item.Id, item.Source, item.Timestamp, Excerpt(item.Text), [], reason));
                }
            }
        }

        // --- Layer 1: deterministic theme groups ---
        var themes = new List<ReportTheme>();
        var structured = items.Where(i => i.Structure is not null).ToList();
        foreach (var group in structured
                     .GroupBy(i => i.Structure!.Department)
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var groupItems = group.ToList();
            var direction = ComputeDirection(groupItems, fromIso, toIso);
            var ids = groupItems.Select(i => i.Id).ToList();

            // --- Layer 2b: Finnish narrative, citation-validated ---
            var narrative = await SynthesizeThemeAsync(group.Key, groupItems, direction, ct);
            if (narrative is { } ok)
            {
                themes.Add(new ReportTheme(group.Key, ok.Title, ok.Narrative, groupItems.Count, direction, ids, true));
            }
            else
            {
                dropped++;
                themes.Add(new ReportTheme(
                    group.Key,
                    FallbackTitle(group.Key, groupItems),
                    FallbackNarrative(groupItems, direction),
                    groupItems.Count,
                    direction,
                    ids,
                    false));
            }
        }

        var report = new ManagementReport(
            fromIso,
            toIso,
            DateTimeOffset.UtcNow.ToString("O"),
            items.Count,
            items.Count(i => i.StructureFailed),
            alerts,
            themes,
            dropped);

        await PersistSnapshotAsync(report, ct);
        return report;
    }

    public async Task<string?> ReadLatestSnapshotJsonAsync(CancellationToken ct)
    {
        var path = Path.Combine(options.Value.SnapshotDir, "report-latest.json");
        return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
    }

    private async Task<List<(string Id, string Reason)>> NominateAlertsAsync(
        IReadOnlyList<StoredFeedback> batch, CancellationToken ct)
    {
        var data = new StringBuilder();
        foreach (var item in batch)
            data.AppendLine($"- [{item.Id}] ({item.Structure?.Department ?? "?"}/{item.Structure?.Severity ?? "?"}) \"{Excerpt(item.Text)}\"");

        var raw = await TryLlmAsync(options.Value.AlertNominationPromptPath, data.ToString(), ct);
        if (raw is null || !LlmJsonExtractor.TryExtractObject(raw, out var doc, out _))
        {
            if (raw is not null)
                logger.LogWarning("Alert nomination output was unparseable; nominations skipped for this batch.");
            return [];
        }
        using (doc)
        {
            if (!doc!.RootElement.TryGetProperty("alerts", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];
            return arr.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object
                    && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                .Select(e => (
                    e.GetProperty("id").GetString()!,
                    e.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                        ? r.GetString()! : "vaatii huomiota"))
                .ToList();
        }
    }

    private async Task<(string Title, string Narrative)?> SynthesizeThemeAsync(
        string department, IReadOnlyList<StoredFeedback> groupItems, string direction, CancellationToken ct)
    {
        var opts = options.Value;
        var data = new StringBuilder();
        data.AppendLine($"osasto: {department}");
        data.AppendLine($"palautteita: {groupItems.Count}");
        data.AppendLine($"suunta: {direction}");
        data.AppendLine("vakavuudet: " + string.Join(", ", groupItems
            .GroupBy(i => i.Structure!.Severity).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} {g.Count()}")));
        data.AppendLine("teemat: " + string.Join(", ", groupItems
            .GroupBy(i => i.Structure!.Theme).OrderByDescending(g => g.Count())
            .Take(6).Select(g => $"{g.Key} ({g.Count()})")));
        data.AppendLine("poimintoja:");
        foreach (var item in groupItems.Take(Math.Min(8, opts.MaxItemsPerLlmCall)))
            data.AppendLine($"- [{item.Id}] \"{Excerpt(item.Text)}\" ({item.Structure!.Severity})");

        var raw = await TryLlmAsync(opts.SynthesisPromptPath, data.ToString(), ct);
        if (raw is null || !LlmJsonExtractor.TryExtractObject(raw, out var doc, out _))
        {
            if (raw is not null)
                logger.LogWarning("Synthesis output for '{Department}' was unparseable; deterministic fallback used.", department);
            return null;
        }
        using (doc)
        {
            var root = doc!.RootElement;
            if (!root.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("narrative", out var narrative) || narrative.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("citedIds", out var cited) || cited.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning("Synthesis for '{Department}' missing required fields; fallback used.", department);
                return null;
            }

            // Grounding gate: every cited id must be one we provided, and there
            // must be at least one — otherwise the narrative is dropped.
            var providedIds = groupItems.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            var citedIds = cited.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
            if (citedIds.Count == 0 || citedIds.Any(id => !providedIds.Contains(id)))
            {
                logger.LogWarning(
                    "Dropped ungrounded synthesis for '{Department}': cited [{Cited}] vs provided {ProvidedCount} ids.",
                    department, string.Join(", ", citedIds), providedIds.Count);
                return null;
            }

            return (title.GetString()!.Trim(), narrative.GetString()!.Trim());
        }
    }

    private async Task<string?> TryLlmAsync(string promptPath, string data, CancellationToken ct)
    {
        var opts = options.Value;
        try
        {
            var template = await File.ReadAllTextAsync(AppPathResolver.Resolve(promptPath), ct);
            var prompt = template.Replace("{{data}}", data, StringComparison.Ordinal);
            var chatOptions = new ChatOptions
            {
                Temperature = opts.SynthesisTemperature,
                MaxOutputTokens = opts.SynthesisMaxOutputTokens > 0 ? opts.SynthesisMaxOutputTokens : null,
            };
            var response = await llmGate.RunAsync(
                innerCt => synthesisClient.GetResponseAsync(prompt, chatOptions, innerCt), ct);
            return response.Text;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // LLM busy/down never blocks the report — layer 1 carries it.
            logger.LogWarning(ex, "LLM synthesis call failed; deterministic layer carries the report.");
            return null;
        }
    }

    /// <summary>First vs second half of the window by volume; "paheneva" when a
    /// growing trend also shifts toward higher severities.</summary>
    private static string ComputeDirection(IReadOnlyList<StoredFeedback> groupItems, string fromIso, string toIso)
    {
        if (groupItems.Count < 3
            || !DateTimeOffset.TryParse(fromIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from)
            || !DateTimeOffset.TryParse(toIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var to))
            return "vakaa";

        var midpoint = from + (to - from) / 2;
        var first = new List<StoredFeedback>();
        var second = new List<StoredFeedback>();
        foreach (var item in groupItems)
        {
            if (!DateTimeOffset.TryParse(item.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
                continue;
            (ts < midpoint ? first : second).Add(item);
        }

        if (second.Count > first.Count * 1.25)
            return AverageSeverityRank(second) > AverageSeverityRank(first) ? "paheneva" : "kasvava";
        if (first.Count > second.Count * 1.25)
            return "laskeva";
        return "vakaa";
    }

    private static double AverageSeverityRank(IReadOnlyList<StoredFeedback> items)
    {
        if (items.Count == 0)
            return 0;
        return items.Average(i => i.Structure!.Severity switch
        {
            "low" => 1, "medium" => 2, "high" => 3, "critical" => 4, _ => 2,
        });
    }

    private static string FallbackTitle(string department, IReadOnlyList<StoredFeedback> groupItems)
    {
        var topTheme = groupItems
            .GroupBy(i => i.Structure!.Theme)
            .OrderByDescending(g => g.Count())
            .First().Key;
        return $"{department}: {topTheme}";
    }

    private static string FallbackNarrative(IReadOnlyList<StoredFeedback> groupItems, string direction)
    {
        var themes = string.Join(", ", groupItems
            .GroupBy(i => i.Structure!.Theme)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"{g.Key} ({g.Count()})"));
        return $"{groupItems.Count} palautetta aikavälillä. Yleisimmät aiheet: {themes}. Suunta: {direction}. (Automaattinen kooste — kielimallin tiivistelmä ei ollut käytettävissä.)";
    }

    private string Excerpt(string text) =>
        text.Length <= options.Value.ExcerptChars
            ? text
            : text[..options.Value.ExcerptChars] + "…";

    private async Task PersistSnapshotAsync(ManagementReport report, CancellationToken ct)
    {
        try
        {
            var dir = options.Value.SnapshotDir;
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(
                Path.Combine(dir, "report-latest.json"), JsonSerializer.Serialize(report, Json), ct);
            await File.WriteAllTextAsync(
                Path.Combine(dir, "report-latest.html"), SnapshotHtml.Render(report), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Snapshot persistence must never fail a live report request.
            logger.LogError(ex, "Snapshot persistence failed; live report unaffected.");
        }
    }
}
