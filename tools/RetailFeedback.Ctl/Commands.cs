using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using RetailFeedback.Generator; // ReportVerifier — reuse the tested acceptance gate

namespace RetailFeedback.Ctl;

public static class Commands
{
    // --- board ---

    public static async Task<int> StatusAsync()
    {
        var rows = await Board.GatherAsync();
        Console.WriteLine(Board.Render(rows));
        return Board.IsLive(rows) ? 0 : 1;
    }

    public static async Task<int> WatchAsync()
    {
        Console.WriteLine(Term.C("  watching — Ctrl-C to stop\n", "2"));
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        while (!cts.IsCancellationRequested)
        {
            var rows = await Board.GatherAsync();
            if (Term.UseColor) Console.Write("[H[J"); // home + clear
            Console.WriteLine(Board.Render(rows));
            try { await Task.Delay(2000, cts.Token); } catch (TaskCanceledException) { break; }
        }
        Console.WriteLine("  stopped watching (nothing changed).");
        return 0;
    }

    // --- lifecycle ---

    public static async Task<int> UpAsync(bool load)
    {
        Console.WriteLine(Term.Bold("\n  bringing the demo up …\n"));
        if (Board.SharedRagUp())
        {
            Console.WriteLine("  " + Term.C("○ REFUSED", "31") +
                " — the shared `mikkonumminendev` RAG is running and the GPU is in use.");
            Console.WriteLine("    Shut that stack down first (its owner's call), then re-run `feedctl up`.");
            return 1;
        }

        // Ollama: start the existing isolated container, or create it via compose.
        Console.WriteLine("  " + Term.C("◐", "33") + " starting isolated ollama …");
        if (Shell.Run("docker", ["start", Config.OllamaContainer], 30000).Code != 0)
            Shell.Run("docker", ["compose", "up", "-d", "ollama"], 120000);
        if (!await WaitAsync(async () => (await Board.GatherAsync()).First(r => r.Label.StartsWith("ollama")).State == Term.State.Ok, 60))
            Console.WriteLine("  " + Term.C("▲ ollama slow to report healthy — continuing", "33"));

        if (!ApiHost.IsRunning())
        {
            if (ApiHost.Start() is null) return 1;
        }
        else Console.WriteLine("  " + Term.C("●", "32") + " API already running");

        Console.WriteLine("  " + Term.C("◐", "33") + " warming Poro (first /health loads the model) …");
        await WaitAsync(async () =>
        {
            var b = await Shell.GetJsonAsync("/health", 15);
            return b is not null && b.Value.TryGetProperty("status", out var s) && s.GetString() == "ok";
        }, 120);

        if (load) await LoadAsync(null);
        Console.WriteLine(Board.Render(await Board.GatherAsync()));
        Console.WriteLine("  open " + Term.C(Config.BaseUrl + "/", "36") + " (management view) · " +
            Term.C(Config.BaseUrl + "/desk.html", "36") + " (desk entry)");
        return 0;
    }

    public static Task<int> DownAsync()
    {
        Console.WriteLine(Term.Bold("\n  taking the demo down …"));
        Console.WriteLine("  " + Term.C("◐", "33") + " stopping API …");
        ApiHost.Stop();
        Console.WriteLine("  " + Term.C("◐", "33") + " stopping ollama (frees the GPU for the shared RAG) …");
        Shell.Run("docker", ["stop", Config.OllamaContainer], 60000);
        Console.WriteLine("  " + Term.C("●", "32") + " down — GPU free.");
        return Task.FromResult(0);
    }

    // --- the one-shot run-through ---

    public static async Task<int> DemoAsync(int seed)
    {
        var variants = File.Exists(Config.Abs("data/corpus/variants.jsonl"))
            ? "data/corpus/variants.jsonl" : "data/corpus/dev-placeholder-variants.jsonl";
        var suffix = variants.Contains("placeholder") ? $"placeholder-{seed}" : seed.ToString(CultureInfo.InvariantCulture);

        Console.WriteLine(Term.Bold($"\n  demo run-through (seed {seed}, pool {Path.GetFileName(variants)})\n"));
        Console.WriteLine("  " + Term.C("◐", "33") + " generate (deterministic, no LLM) …");
        var gen = Shell.Run("dotnet", ["run", "--project", Config.Abs(Config.GeneratorProject), "--", "generate",
            "--seed", seed.ToString(CultureInfo.InvariantCulture),
            "--Generator:VariantsPath=" + variants, "--Generator:NoiseCount=15"], 180000);
        if (gen.Code != 0) { Console.WriteLine(Term.C("  ○ generate failed", "31")); Console.WriteLine(gen.Output); return 1; }

        if (!Board.IsLive(await Board.GatherAsync()))
        {
            var up = await UpAsync(load: false);
            if (up != 0) return up;
        }

        var corpus = $"data/corpus/generated-{suffix}.jsonl";
        var loaded = await LoadAsync(corpus);
        if (loaded != 0) return loaded;
        await ReportAsync(30);
        return await VerifyAsync($"data/corpus/ground-truth-{suffix}.json");
    }

    // --- data + analysis verbs ---

    public static async Task<int> InterpretAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Console.WriteLine(Term.C("  usage: interpret \"asiakas sano et maitokaapis oli vanhoi purkkei\"", "33"));
            return 1;
        }
        Console.WriteLine($"\n  {Term.Bold("desk /interpret")} · \"{text}\"");
        var sw = Stopwatch.StartNew();
        var (status, body) = await Shell.PostJsonAsync("/interpret", JsonSerializer.Serialize(new { text }), 120);
        sw.Stop();
        if (status == 503) { Console.WriteLine(Term.C("  ▲ busy (503) — GPU shed the request; retry", "33")); return 1; }
        if (status != 200) { Console.WriteLine(Term.C($"  ○ /interpret HTTP {status}: {body}", "31")); return 1; }
        using var doc = JsonDocument.Parse(body);
        var r = doc.RootElement;
        if (r.TryGetProperty("failed", out var f) && f.GetBoolean())
        {
            Console.WriteLine($"  {Term.C("▲", "33")} model could not structure it ({sw.Elapsed.TotalSeconds:F1}s) — the desk would fall back to manual entry.\n");
            return 0;
        }
        var s = r.GetProperty("structure");
        var salvaged = r.TryGetProperty("salvaged", out var sv) && sv.GetBoolean() ? Term.C(" (salvaged)", "2") : "";
        Console.WriteLine($"  {Term.C("→", "32")} {s.GetProperty("department").GetString()} / " +
            $"\"{s.GetProperty("theme").GetString()}\" / {s.GetProperty("severity").GetString()} / " +
            $"{s.GetProperty("type").GetString()} / {s.GetProperty("language").GetString()}" +
            $"   {Term.C($"{sw.Elapsed.TotalSeconds:F1}s", "2")}{salvaged}\n");
        return 0;
    }

    public static async Task<int> LoadAsync(string? corpus)
    {
        corpus ??= File.Exists(Config.Abs("data/corpus/generated-placeholder-42.jsonl"))
            ? "data/corpus/generated-placeholder-42.jsonl" : "data/corpus/dev-placeholder-variants.jsonl";
        var path = Config.Abs(corpus);
        if (!File.Exists(path)) { Console.WriteLine(Term.C($"  ○ corpus not found: {corpus}", "31")); return 1; }

        int created = 0, dup = 0, failed = 0, i = 0;
        Console.WriteLine("  " + Term.C("◐", "33") + $" ingesting {Path.GetFileName(corpus)} through POST /feedback …");
        foreach (var line in await File.ReadAllLinesAsync(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            var body = JsonSerializer.Serialize(new
            {
                id = r.GetProperty("id").GetString(),
                source = r.GetProperty("source").GetString(),
                text = r.GetProperty("text").GetString(),
                timestamp = r.GetProperty("timestamp").GetString(),
            });
            var (status, _) = await Shell.PostJsonAsync("/feedback", body, 120);
            if (status is 201 or 200) created++;
            else if (status == 409) dup++;
            else { failed++; if (failed <= 3) Console.WriteLine(Term.C($"    item {r.GetProperty("id").GetString()}: HTTP {status}", "33")); }
            if (++i % 10 == 0) Console.Write(".");
        }
        Console.WriteLine();
        Console.WriteLine($"  {Term.C("●", "32")} ingested: {created} created · {dup} already present · {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    public static async Task<int> ReportAsync(int days)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-Math.Clamp(days, 1, 92));
        var q = $"/report?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
        Console.WriteLine("  " + Term.C("◐", "33") + " generating report (live Finnish synthesis) …");
        var sw = Stopwatch.StartNew();
        var body = await Shell.GetJsonAsync(q, 300);
        sw.Stop();
        if (body is null) { Console.WriteLine(Term.C("  ○ report failed (API up? warmed?)", "31")); return 1; }
        var rep = body.Value;
        int Items(string k) => rep.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        Console.WriteLine($"\n  {Term.Bold("report")} · {sw.Elapsed.TotalSeconds:F1}s · {Items("totalItems")} items · " +
            $"{rep.GetProperty("alerts").GetArrayLength()} alert(s) · {rep.GetProperty("themes").GetArrayLength()} theme(s) · " +
            $"{Items("droppedClaimCount")} ungrounded dropped · {Items("llmFallbackCount")} llm-fallback");
        foreach (var a in rep.GetProperty("alerts").EnumerateArray())
        {
            var reason = a.TryGetProperty("llmReason", out var lr) && lr.ValueKind == JsonValueKind.String
                ? "LLM: " + lr.GetString()
                : "keyword: " + string.Join(",", a.GetProperty("deterministicHits").EnumerateArray().Select(h => h.GetProperty("pattern").GetString()));
            Console.WriteLine($"    {Term.C("▲ ALERT", "31")} [{a.GetProperty("feedbackId").GetString()}] {reason}");
        }
        foreach (var t in rep.GetProperty("themes").EnumerateArray().Take(6))
            Console.WriteLine($"    {Term.C("●", "32")} {t.GetProperty("department").GetString(),-22} " +
                $"({t.GetProperty("count").GetInt32()}, {t.GetProperty("direction").GetString()})  {t.GetProperty("title").GetString()}");
        Console.WriteLine();
        return 0;
    }

    public static async Task<int> VerifyAsync(string? groundTruth)
    {
        groundTruth ??= "data/corpus/ground-truth-placeholder-42.json";
        var gtPath = Config.Abs(groundTruth);
        var snapPath = Config.Abs(Config.SnapshotJson);
        if (!File.Exists(gtPath)) { Console.WriteLine(Term.C($"  ○ ground truth not found: {groundTruth}", "31")); return 2; }
        if (!File.Exists(snapPath)) { Console.WriteLine(Term.C("  ○ no report snapshot yet — run `report` first", "31")); return 2; }

        List<ReportVerifier.StoryResult> results;
        try
        {
            results = ReportVerifier.Verify(await File.ReadAllTextAsync(gtPath), await File.ReadAllTextAsync(snapPath));
        }
        catch (Exception ex) { Console.WriteLine(Term.C("  ○ " + ex.Message, "31")); return 2; }

        var allPass = true;
        Console.WriteLine();
        foreach (var r in results)
        {
            allPass &= r.Pass;
            var tag = r.Pass ? Term.C("PASS", "32") : Term.C("FAIL", "31");
            var window = r.WindowCovered ? "" : Term.C(" · WINDOW MISMATCH", "31");
            var trend = r.TrendOk ? "ok" : Term.C("diluted", "33");
            Console.WriteLine($"  {tag} {r.StoryId}: grounding {r.GroundedIds}/{r.RequiredIds}{window} · " +
                $"trend {r.ExpectedTrend}→{r.ReportedDirection} {trend}" +
                (r.AlertExpected ? $" · alert {(r.AlertPass ? "present" : Term.C("MISSING", "31"))}" : ""));
        }
        Console.WriteLine(allPass
            ? "\n  " + Term.C("ACCEPTANCE: PASS", "32") + " — every planted story is grounded.\n"
            : "\n  " + Term.C("ACCEPTANCE: FAIL", "31") + "\n");
        return allPass ? 0 : 1;
    }

    public static async Task<int> TelemetryAsync(int days)
    {
        var to = DateTimeOffset.UtcNow;
        var from = to.AddDays(-Math.Clamp(days, 1, 92));
        var body = await Shell.GetJsonAsync($"/telemetry/corrections?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}", 15);
        if (body is null) { Console.WriteLine(Term.C("  ○ telemetry unavailable (API up?)", "31")); return 1; }
        var t = body.Value;
        Console.WriteLine($"\n  {Term.Bold("correction telemetry")} · desk entries {t.GetProperty("deskEntries").GetInt32()} " +
            $"(interpreted {t.GetProperty("modelInterpreted").GetInt32()}, model-failed {t.GetProperty("modelFailed").GetInt32()})");
        var any = false;
        foreach (var f in t.GetProperty("perField").EnumerateArray().Where(f => f.GetProperty("corrections").GetInt32() > 0))
        {
            any = true;
            Console.WriteLine($"    {f.GetProperty("field").GetString(),-12} {f.GetProperty("corrections").GetInt32()} correction(s) · rate {f.GetProperty("rate").GetDouble():0.###}");
        }
        if (!any) Console.WriteLine(Term.C("    no field corrections in this window", "2"));
        Console.WriteLine();
        return 0;
    }

    public static async Task<int> LogsAsync(int n)
    {
        var body = await Shell.GetJsonAsync($"/feedback?limit={Math.Clamp(n, 1, 200)}", 15);
        if (body is null || body.Value.ValueKind != JsonValueKind.Array) { Console.WriteLine(Term.C("  ○ no data (API up?)", "31")); return 1; }
        Console.WriteLine(Term.Bold($"\n  last {body.Value.GetArrayLength()} ingested item(s):\n"));
        foreach (var it in body.Value.EnumerateArray())
        {
            var failed = it.TryGetProperty("structureFailed", out var sf) && sf.GetBoolean();
            var dept = it.TryGetProperty("structure", out var st) && st.ValueKind == JsonValueKind.Object
                ? st.GetProperty("department").GetString() : (failed ? Term.C("structure_failed", "31") : "?");
            var text = it.GetProperty("text").GetString() ?? "";
            if (text.Length > 60) text = text[..60] + "…";
            Console.WriteLine($"    {Term.C(it.GetProperty("source").GetString() ?? "?", "36"),-16} {dept,-22} {text}");
        }
        Console.WriteLine();
        return 0;
    }

    public static int Open()
    {
        try { Process.Start(new ProcessStartInfo(Config.BaseUrl + "/") { UseShellExecute = true }); }
        catch (Exception ex) { Console.WriteLine(Term.C("  ○ " + ex.Message, "31")); return 1; }
        Console.WriteLine("  opened " + Term.C(Config.BaseUrl + "/", "36"));
        return 0;
    }

    private static async Task<bool> WaitAsync(Func<Task<bool>> ready, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await ready()) return true;
            await Task.Delay(1500);
        }
        return false;
    }
}
