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
/// keyword-less items, under a per-report call budget so one refresh can never
/// starve the desk path on the shared GPU gate. Every LLM claim must cite
/// provided feedback IDs; a narrative whose citations fail is DROPPED to a
/// deterministic fallback and counted — the view never shows an ungrounded
/// claim. LLM unavailability is a separate, honestly-labeled counter.
/// </summary>
public sealed class ReportService(
    FeedbackStore store,
    [FromKeyedServices(LlmServiceCollectionExtensions.SynthesisKey)] IChatClient synthesisClient,
    LlmGate llmGate,
    ReportCache cache,
    IOptions<ReportOptions> options,
    ILogger<ReportService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Single-flight: concurrent refreshes of the same view wait for one
    // generation instead of doubling the LLM load.
    private readonly SemaphoreSlim _generationLock = new(1, 1);

    private sealed class GenState
    {
        public int LlmCallsRemaining;
        public int DroppedClaims;
        public int LlmFallbacks;
    }

    public async Task<ManagementReport> GenerateAsync(string fromIso, string toIso, CancellationToken ct)
    {
        var key = fromIso + "|" + toIso;
        if (cache.TryGet(key, out var cached))
            return cached;

        await _generationLock.WaitAsync(ct);
        try
        {
            if (cache.TryGet(key, out cached))
                return cached;
            var report = await GenerateCoreAsync(fromIso, toIso, ct);
            if (options.Value.ReportCacheSeconds > 0)
                cache.Set(key, report, TimeSpan.FromSeconds(options.Value.ReportCacheSeconds));
            return report;
        }
        finally
        {
            _generationLock.Release();
        }
    }

    private async Task<ManagementReport> GenerateCoreAsync(string fromIso, string toIso, CancellationToken ct)
    {
        var opts = options.Value;
        var items = await store.QueryAsync(fromIso, toIso, opts.MaxItemsPerWindow, ct);
        var state = new GenState { LlmCallsRemaining = opts.MaxLlmCallsPerReport };

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
            var batches = candidates.Chunk(opts.MaxItemsPerLlmCall).ToList();
            foreach (var batch in batches)
            {
                if (state.LlmCallsRemaining <= 0)
                {
                    // No silent caps: the skipped coverage is logged.
                    logger.LogWarning(
                        "LLM call budget exhausted; {Remaining} nomination batches skipped this report.",
                        batches.Count - batches.IndexOf(batch));
                    break;
                }
                var nominated = await NominateAlertsAsync(batch, state, ct);
                foreach (var (id, reason) in nominated)
                {
                    var item = batch.FirstOrDefault(i => i.Id == id);
                    if (item is null)
                    {
                        state.DroppedClaims++;
                        logger.LogWarning("Dropped LLM alert nomination for unknown id '{Id}' — not in the provided batch.", id);
                        continue;
                    }
                    alerts.Add(new ReportAlert(item.Id, item.Source, item.Timestamp, Excerpt(item.Text), [], reason));
                }
            }
        }

        // --- Layer 1: deterministic theme groups; Layer 2b: cited narratives ---
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

            var narrative = await SynthesizeThemeAsync(group.Key, groupItems, direction, state, ct);
            themes.Add(narrative is { } ok
                ? new ReportTheme(group.Key, ok.Title, ok.Narrative, groupItems.Count, direction, ids, true)
                : new ReportTheme(
                    group.Key,
                    FallbackTitle(group.Key, groupItems),
                    FallbackNarrative(groupItems, direction),
                    groupItems.Count,
                    direction,
                    ids,
                    false));
        }

        var report = new ManagementReport(
            fromIso,
            toIso,
            DateTimeOffset.UtcNow.ToString("O"),
            items.Count,
            items.Count(i => i.StructureFailed),
            alerts,
            themes,
            state.DroppedClaims,
            state.LlmFallbacks);

        await PersistSnapshotAsync(report, ct);
        return report;
    }

    public async Task<string?> ReadLatestSnapshotJsonAsync(CancellationToken ct)
    {
        var path = Path.Combine(options.Value.SnapshotDir, "report-latest.json");
        return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
    }

    public string? LatestSnapshotHtmlPath()
    {
        var path = Path.Combine(options.Value.SnapshotDir, "report-latest.html");
        return File.Exists(path) ? Path.GetFullPath(path) : null;
    }

    private async Task<List<(string Id, string Reason)>> NominateAlertsAsync(
        IReadOnlyList<StoredFeedback> batch, GenState state, CancellationToken ct)
    {
        var data = new StringBuilder();
        foreach (var item in batch)
            data.AppendLine($"- [{item.Id}] ({item.Structure?.Department ?? "?"}/{item.Structure?.Severity ?? "?"}) \"{Excerpt(item.Text)}\"");

        var raw = await TryLlmAsync(options.Value.AlertNominationPromptPath, data.ToString(), state, ct);
        if (raw is null)
            return [];
        if (!LlmJsonExtractor.TryExtractObject(raw, out var doc, out _))
        {
            state.LlmFallbacks++;
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
        string department, IReadOnlyList<StoredFeedback> groupItems, string direction, GenState state, CancellationToken ct)
    {
        var opts = options.Value;
        if (state.LlmCallsRemaining <= 0)
        {
            state.LlmFallbacks++;
            logger.LogInformation("LLM budget exhausted; deterministic fallback for '{Department}'.", department);
            return null;
        }

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

        var raw = await TryLlmAsync(opts.SynthesisPromptPath, data.ToString(), state, ct);
        if (raw is null)
            return null;
        if (!LlmJsonExtractor.TryExtractObject(raw, out var doc, out _))
        {
            state.LlmFallbacks++;
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
                state.LlmFallbacks++;
                logger.LogWarning("Synthesis for '{Department}' missing required fields; fallback used.", department);
                return null;
            }

            // Grounding gate: every cited id must be one we provided, and there
            // must be at least one — otherwise the claim is dropped and counted
            // as exactly that: an ungrounded claim.
            var providedIds = groupItems.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            var citedIds = cited.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
            if (citedIds.Count == 0 || citedIds.Any(id => !providedIds.Contains(id)))
            {
                state.DroppedClaims++;
                logger.LogWarning(
                    "Dropped ungrounded synthesis for '{Department}': cited [{Cited}] vs provided {ProvidedCount} ids.",
                    department, string.Join(", ", citedIds), providedIds.Count);
                return null;
            }

            return (title.GetString()!.Trim(), narrative.GetString()!.Trim());
        }
    }

    /// <summary>Returns the raw LLM text, or null when the model could not be
    /// reached (unavailable/busy) — which is counted as a fallback, never as a
    /// dropped claim.</summary>
    private async Task<string?> TryLlmAsync(string promptPath, string data, GenState state, CancellationToken ct)
    {
        var opts = options.Value;
        state.LlmCallsRemaining--;
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
            state.LlmFallbacks++;
            logger.LogWarning(ex, "LLM call failed; deterministic layer carries the report.");
            return null;
        }
    }

    /// <summary>First vs second half of the window by volume. "paheneva" needs
    /// BOTH growth and a severity shift measured against a non-empty early half
    /// — a theme that only just appeared can be "kasvava", never "paheneva".</summary>
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
            return first.Count > 0 && AverageSeverityRank(second) > AverageSeverityRank(first)
                ? "paheneva"
                : "kasvava";
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
            // Atomic temp-then-rename: a concurrent snapshot reader must never
            // observe a truncated file.
            await WriteAtomicAsync(Path.Combine(dir, "report-latest.json"), JsonSerializer.Serialize(report, Json), ct);
            await WriteAtomicAsync(Path.Combine(dir, "report-latest.html"), SnapshotHtml.Render(report), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Snapshot persistence must never fail a live report request.
            logger.LogError(ex, "Snapshot persistence failed; live report unaffected.");
        }
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, content, ct);
        File.Move(temp, path, overwrite: true);
    }
}
