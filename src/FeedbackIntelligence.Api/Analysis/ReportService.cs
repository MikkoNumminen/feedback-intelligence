using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FeedbackIntelligence.Api.Storage;
using FeedbackIntelligence.Core.Domain;
using FeedbackIntelligence.Core.Security;
using FeedbackIntelligence.Llm;
using FeedbackIntelligence.Llm.Structuring;

namespace FeedbackIntelligence.Api.Analysis;

/// <summary>
/// Two-layer analysis. Layer 1 is deterministic and always succeeds: window
/// query, alert collection, category grouping, counts, trend direction.
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
    IActiveDomain activeDomain,
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
        public int AlertVerifiesRemaining;
        public int DroppedClaims;
        public int LlmFallbacks;
        // A3: narratives dropped for turning directive (recommend/act/verdict)
        // instead of describing — an injection-defense drop, distinct from an
        // ungrounded-citation drop.
        public int ActionDropped;
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
            // Capture the invalidation epoch BEFORE reading items: if a desk ingest
            // invalidates the cache during the ~20 s generation, Set no-ops and the
            // stale report is not cached — the live-desk-entry moment stays correct.
            var epoch = cache.Epoch;
            var report = await GenerateCoreAsync(fromIso, toIso, ct);
            if (options.Value.ReportCacheSeconds > 0)
                cache.Set(key, report, TimeSpan.FromSeconds(options.Value.ReportCacheSeconds), epoch);
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
        var state = new GenState
        {
            LlmCallsRemaining = opts.MaxLlmCallsPerReport,
            AlertVerifiesRemaining = opts.MaxAlertVerifyCalls,
        };

        // --- Layer 1: deterministic alerts, always present ---
        var alerts = items
            .Where(i => i.Alerts.Count > 0)
            .OrderByDescending(i => i.Timestamp, StringComparer.Ordinal)
            .Select(i => new ReportAlert(i.Id, i.Source, i.Timestamp, Excerpt(i.Text), i.Text, i.Alerts, null))
            .ToList();

        // --- Layer 2a: LLM alert screen over keyword-less items (may ADD, never removes) ---
        // Poro-8B discriminates safety reliably on ONE item as a strict yes/no,
        // but both floods AND misses when asked to select from a list — so the
        // reliable path is to screen every keyword-less item INDIVIDUALLY (recall +
        // precision in one deterministic pass). Praise is never a safety alert;
        // everything else stays in scope — a real hazard can be typed
        // complaint/question/suggestion/other, and an item the model failed to
        // structure could still be a safety report. A zero LLM budget means
        // deterministic-only mode — the screen is skipped too.
        if (opts.AlertNominationEnabled && state.LlmCallsRemaining > 0)
        {
            var candidates = items
                .Where(i => i.Alerts.Count == 0 && (i.Structure is null || i.Structure.Type != "praise"))
                .ToList();
            var confirmed = new List<StoredFeedback>();
            foreach (var item in candidates)
            {
                if (state.AlertVerifiesRemaining <= 0)
                {
                    // No silent caps: the unscreened tail is logged — unless the
                    // screen was deliberately disabled with MaxAlertVerifyCalls=0.
                    if (opts.MaxAlertVerifyCalls > 0)
                        logger.LogWarning("Alert-screen budget exhausted; {N} keyword-less item(s) not screened.",
                            candidates.Count - candidates.IndexOf(item));
                    break;
                }
                if (await VerifyAlertAsync(item, state, ct))
                    confirmed.Add(item);
            }

            // One nomination call over ONLY the confirmed items yields a grounded
            // reason each; fall back to a LOCALIZED generic line if the model omits
            // one (the yes/no screen already decided it IS an alert).
            var reasons = confirmed.Count > 0 && state.LlmCallsRemaining > 0
                ? (await NominateAlertsAsync(confirmed, state, ct))
                    .GroupBy(n => n.Id, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First().Reason, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in confirmed)
                alerts.Add(new ReportAlert(item.Id, item.Source, item.Timestamp, Excerpt(item.Text), item.Text, [],
                    // A3: the alert reason is another model-authored, manager-facing
                    // slot. A directive one (recommend/act/verdict) falls back to the
                    // deterministic line — an injected instruction has no slot here
                    // either. A genuine safety reason describes the hazard, not an act.
                    reasons.TryGetValue(item.Id, out var r) && !string.IsNullOrWhiteSpace(r)
                            && !NarrativeGuard.LooksActionBearing(r)
                        ? r
                        : ReportText.PossibleSafetyAlert(activeDomain.Descriptor.Language)));
        }

        // --- Layer 1: deterministic theme groups; Layer 2b: cited narratives ---
        var lang = activeDomain.Descriptor.Language;
        var themes = new List<ReportTheme>();
        var structured = items.Where(i => i.Structure is not null).ToList();
        foreach (var group in structured
                     .GroupBy(i => i.Structure!.Category)
                     .OrderByDescending(g => g.Count())
                     .ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var groupItems = group.ToList();
            var direction = ComputeDirection(groupItems, fromIso, toIso, opts.MinItemsForTrend, opts.TrendSignificanceZ);
            var directionLabel = ReportText.DirectionLabel(direction, lang);
            var ids = groupItems.Select(i => i.Id).ToList();
            var sources = BuildSources(groupItems);
            // Injection hardening (ADR-0021 A2): flagged items stay IN the group and
            // trend (excluding them would be exploitable), but the count surfaces so
            // the view can warn a manager that the group holds possibly-manipulated
            // items driving its number/direction.
            var flaggedCount = groupItems.Count(i => i.NeedsReview);

            var narrative = await SynthesizeThemeAsync(group.Key, groupItems, directionLabel, state, ct);
            themes.Add(narrative is { } ok
                ? new ReportTheme(group.Key, ok.Title, ok.Narrative, groupItems.Count, direction, directionLabel, ids, true, sources, flaggedCount)
                : new ReportTheme(
                    group.Key,
                    FallbackTitle(group.Key, groupItems),
                    FallbackNarrative(groupItems, directionLabel, lang),
                    groupItems.Count,
                    direction,
                    directionLabel,
                    ids,
                    false,
                    sources,
                    flaggedCount));
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
            state.LlmFallbacks,
            lang,
            state.ActionDropped);

        await PersistSnapshotAsync(report, ct);
        return report;
    }

    public Task<string?> ReadLatestSnapshotJsonAsync(CancellationToken ct) =>
        ReadSharedAsync(Path.Combine(options.Value.SnapshotDir, "report-latest.json"), ct);

    public Task<string?> ReadLatestSnapshotHtmlAsync(CancellationToken ct) =>
        ReadSharedAsync(Path.Combine(options.Value.SnapshotDir, "report-latest.html"), ct);

    /// <summary>Read a snapshot file tolerant of a concurrent atomic replace: open with
    /// FileShare.ReadWrite|Delete so the writer's File.Replace is never blocked, and
    /// treat a transient sharing violation as "no snapshot yet" rather than a 500 on the
    /// degraded-mode fallback endpoint.</summary>
    private static async Task<string?> ReadSharedAsync(string path, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            await using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs);
            return await reader.ReadToEndAsync(ct);
        }
        catch (IOException)
        {
            return null; // mid-replace: fall back to "no snapshot", never surface a 500
        }
    }

    private async Task<List<(string Id, string Reason)>> NominateAlertsAsync(
        IReadOnlyList<StoredFeedback> batch, GenState state, CancellationToken ct)
    {
        var data = new StringBuilder();
        foreach (var item in batch)
            data.AppendLine($"- [{item.Id}] ({item.Structure?.Category ?? "?"}/{item.Structure?.Severity ?? "?"}) \"{UntrustedText.Neutralize(Excerpt(item.Text))}\"");

        var raw = await TryLlmAsync(activeDomain.PromptPath("alertNomination"), data.ToString(), state, ct);
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
                    // Empty (not a hardcoded Finnish string) when the model omits a
                    // reason — the caller substitutes a LOCALIZED generic line, so a
                    // non-fi domain never renders Finnish here.
                    e.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                        ? r.GetString()! : ""))
                .ToList();
        }
    }

    private static readonly char[] AnswerSeparators =
        { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ':', ';', '"', '\'', '-', '—', '(', ')' };

    /// <summary>The per-item safety screen: is THIS one item an immediate safety
    /// alert? Poro answers a focused yes/no ("kyllä"/"ei") reliably even though it
    /// floods AND misses when selecting from a list. Tiny output, on its own budget.
    /// The reply is matched by WHOLE WORD in the domain's language (fi kyllä/ei, en
    /// yes/no): an explicit negative token rejects, an explicit affirmative confirms,
    /// anything else — empty, a hedge, the Finnish filler "no" (= "well") — is NOT an
    /// alert. Whole-word matching avoids "no" reading as a negative and an empty reply
    /// reading as a keep; the genuine safety case answers a clean affirmative, so this
    /// costs no real recall. An UNREACHABLE model fails CLOSED (see the catch): a model
    /// outage yields no LLM alerts rather than one on every item.</summary>
    private async Task<bool> VerifyAlertAsync(StoredFeedback item, GenState state, CancellationToken ct)
    {
        state.AlertVerifiesRemaining--;
        try
        {
            var template = await AppPathResolver.ReadPromptAsync(activeDomain.PromptPath("alertVerify"), ct);
            // Neutralize: block the audited breakout — text that closes the
            // Palaute:"…" quote and fakes a "Vastaus: kyllä" line (ADR-0021).
            var prompt = template.Replace("{{text}}", UntrustedText.Neutralize(item.Text), StringComparison.Ordinal);
            var chatOptions = new ChatOptions { Temperature = 0, MaxOutputTokens = 12 };
            var response = await llmGate.RunAsync(
                innerCt => synthesisClient.GetResponseAsync(prompt, chatOptions, innerCt), ct);
            // A natural yes/no reads Poro's judgment far better than structured output:
            // asked for {"alert": …} JSON it defaulted even the real safety case to
            // false, but asked "kyllä vai ei?" it is exact (see ADR-0015).
            var (yes, no) = activeDomain.Descriptor.Language == "fi" ? ("kyllä", "ei") : ("yes", "no");
            var tokens = response.Text.ToLowerInvariant().Split(AnswerSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Contains(no, StringComparer.Ordinal)) return false;   // explicit "ei"/"no" → not an alert
            return tokens.Contains(yes, StringComparer.Ordinal);            // explicit "kyllä"/"yes" → alert; else no
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail CLOSED on an unreachable model: produce NO LLM alert (the
            // deterministic keyword layer still carries safety — ADR-0009). Failing
            // open here would turn a model outage into an alert on every complaint.
            logger.LogWarning(ex, "Alert screen call failed for '{Id}'; no LLM alert from it this report.", item.Id);
            return false;
        }
    }

    private async Task<(string Title, string Narrative)?> SynthesizeThemeAsync(
        string category, IReadOnlyList<StoredFeedback> groupItems, string directionLabel, GenState state, CancellationToken ct)
    {
        var opts = options.Value;
        if (state.LlmCallsRemaining <= 0)
        {
            state.LlmFallbacks++;
            logger.LogInformation("LLM budget exhausted; deterministic fallback for '{Category}'.", category);
            return null;
        }

        var s = ReportText.Synthesis(activeDomain.Descriptor.Language);
        var data = new StringBuilder();
        data.AppendLine($"{activeDomain.Descriptor.CategoryFieldLabel}: {category}");
        data.AppendLine($"{s.Count}: {groupItems.Count}");
        data.AppendLine($"{s.Trend}: {directionLabel}");
        data.AppendLine($"{s.Severities}: " + string.Join(", ", groupItems
            .GroupBy(i => i.Structure!.Severity).OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key} {g.Count()}")));
        data.AppendLine($"{s.Themes}: " + string.Join(", ", groupItems
            .GroupBy(i => i.Structure!.Theme).OrderByDescending(g => g.Count())
            .Take(6).Select(g => $"{UntrustedText.Neutralize(g.Key)} ({g.Count()})")));
        data.AppendLine($"{s.Excerpts}:");
        // Untrusted excerpts + the model-produced theme are neutralized before they
        // enter this prompt: no quote breakout, no forged "- [id]" rows (ADR-0021).
        foreach (var item in groupItems.Take(Math.Min(8, opts.MaxItemsPerLlmCall)))
            data.AppendLine($"- [{item.Id}] \"{UntrustedText.Neutralize(Excerpt(item.Text))}\" ({item.Structure!.Severity})");

        var raw = await TryLlmAsync(activeDomain.PromptPath("synthesis"), data.ToString(), state, ct);
        if (raw is null)
            return null;
        if (!LlmJsonExtractor.TryExtractObject(raw, out var doc, out _))
        {
            state.LlmFallbacks++;
            logger.LogWarning("Synthesis output for '{Category}' was unparseable; deterministic fallback used.", category);
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
                logger.LogWarning("Synthesis for '{Category}' missing required fields; fallback used.", category);
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
                    "Dropped ungrounded synthesis for '{Category}': cited [{Cited}] vs provided {ProvidedCount} ids.",
                    category, string.Join(", ", citedIds), providedIds.Count);
                return null;
            }

            // A3: bound the synthesis authority to a grounded DESCRIPTION. If EITHER
            // the title or the narrative turned directive (recommend/act/verdict) —
            // the shape an injected "erota osastopäällikkö" produces — drop the whole
            // tuple to the deterministic fallback so the instruction has no output
            // slot (the title is a prominent, ≤8-word manager-facing slot too).
            // Backstop to the prompt constraint; counted separately from ungrounded.
            var narrativeText = narrative.GetString()!.Trim();
            var titleText = title.GetString()!.Trim();
            if (NarrativeGuard.LooksActionBearing(narrativeText) || NarrativeGuard.LooksActionBearing(titleText))
            {
                state.ActionDropped++;
                logger.LogWarning(
                    "Dropped action-bearing synthesis for '{Category}' to fallback (A3): title or narrative was directive, not descriptive.",
                    category);
                return null;
            }

            return (titleText, narrativeText);
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
            var template = await AppPathResolver.ReadPromptAsync(promptPath, ct);
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

    /// <summary>First vs second half of the window. Splits the group at the window
    /// midpoint and defers the actual decision to <see cref="TrendDirection"/>,
    /// which only reports a trend when the volume shift is statistically
    /// significant — the guard that stops organic noise from being labelled a
    /// trend (measured 86% false-trend rate under the old 1.25x rule; see
    /// ADR-0017).</summary>
    // Neutral direction KEYS (stable/growing/declining/worsening); the localized
    // label is applied at presentation time via ReportText so the verify gate and
    // JSON stay language-independent.
    private static string ComputeDirection(
        IReadOnlyList<StoredFeedback> groupItems, string fromIso, string toIso, int minItems, double z)
    {
        if (groupItems.Count < minItems
            || !DateTimeOffset.TryParse(fromIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var from)
            || !DateTimeOffset.TryParse(toIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var to))
            return "stable";

        var midpoint = from + (to - from) / 2;
        var first = new List<StoredFeedback>();
        var second = new List<StoredFeedback>();
        foreach (var item in groupItems)
        {
            if (!DateTimeOffset.TryParse(item.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
                continue;
            (ts < midpoint ? first : second).Add(item);
        }

        return TrendDirection(
            first.Count, second.Count, AverageSeverityRank(first), AverageSeverityRank(second), minItems, z);
    }

    /// <summary>Pure trend decision over a first/second-half split. A volume shift
    /// is only a trend when it clears <paramref name="z"/> standard deviations of
    /// what uniform-in-time arrivals would produce: under H0 the second-half count
    /// is ~Binomial(n, 0.5), so the (second − first) gap has sd √n and a trend needs
    /// |second − first| ≥ z·√n. Groups below <paramref name="minItems"/> are always
    /// "stable" (too few to tell signal from noise). "worsening" additionally needs
    /// a rising average severity over a NON-EMPTY early half — a theme that only
    /// just appeared can grow, never worsen. Neutral keys; localized at
    /// presentation. Internal for direct measurement in tests.</summary>
    internal static string TrendDirection(
        int first, int second, double firstSeverity, double secondSeverity, int minItems, double z)
    {
        var n = first + second;
        if (n < minItems)
            return "stable";

        var margin = z * Math.Sqrt(n);
        var delta = second - first;
        if (delta >= margin)
            return first > 0 && secondSeverity > firstSeverity ? "worsening" : "growing";
        if (-delta >= margin)
            return "declining";
        return "stable";
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

    /// <summary>The group's messages, embedded in the report so the view shows the
    /// evidence in one click (live OR from a snapshot, no per-item fetch). Ordered
    /// most-severe-then-most-recent first so the serious voices lead; Text is the
    /// full stored message (length-capped at ingest).</summary>
    private static List<ReportSourceItem> BuildSources(IReadOnlyList<StoredFeedback> items) =>
        items
            .OrderByDescending(i => SeverityRank(i.Structure?.Severity))
            .ThenByDescending(i => i.Timestamp, StringComparer.Ordinal)
            .Select(i => new ReportSourceItem(
                i.Id, i.Source, i.Timestamp, i.Text, i.Structure?.Severity ?? "unknown", i.NeedsReview))
            .ToList();

    private static int SeverityRank(string? severity) => severity switch
    {
        "critical" => 4, "high" => 3, "medium" => 2, "low" => 1, _ => 0,
    };

    private static string FallbackTitle(string category, IReadOnlyList<StoredFeedback> groupItems)
    {
        var topTheme = groupItems
            .GroupBy(i => i.Structure!.Theme)
            .OrderByDescending(g => g.Count())
            .First().Key;
        return $"{category}: {topTheme}";
    }

    private string FallbackNarrative(IReadOnlyList<StoredFeedback> groupItems, string directionLabel, string language)
    {
        var themes = string.Join(", ", groupItems
            .GroupBy(i => i.Structure!.Theme)
            .OrderByDescending(g => g.Count())
            .Take(3)
            .Select(g => $"{g.Key} ({g.Count()})"));
        return ReportText.FallbackNarrative(groupItems.Count, themes, directionLabel, language);
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
            await WriteAtomicAsync(Path.Combine(dir, "report-latest.html"), SnapshotHtml.Render(report, activeDomain.Descriptor.Language), ct);
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

        // Windows: File.Move(overwrite) is MoveFileEx(REPLACE_EXISTING) — it unlinks the
        // destination and throws a sharing violation if a reader has it open (readers
        // here open with FileShare.Read, not Delete). File.Replace is NTFS's
        // replace-while-open primitive and keeps an in-flight reader's handle valid; a
        // bounded retry rides out a transient violation so the snapshot is never
        // silently left stale. On a first write (no destination yet) fall back to Move.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(path))
                    File.Replace(temp, path, destinationBackupFileName: null);
                else
                    File.Move(temp, path);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(40, ct);
            }
        }
    }
}
