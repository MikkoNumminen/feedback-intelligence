using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FeedbackIntelligence.Generator; // ReportVerifier — reuse the tested acceptance gate

namespace FeedbackIntelligence.Ctl;

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
        // Store the handler and remove it in finally: an unremoved handler over a disposed
        // CTS throws ObjectDisposedException on the cancel thread on a later Ctrl-C (e.g.
        // watch → Ctrl-C → up --supervise → Ctrl-C in the REPL).
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var rows = await Board.GatherAsync();
                Console.Write(Term.Clear);
                Console.WriteLine(Board.Render(rows));
                try { await Task.Delay(2000, cts.Token); } catch (TaskCanceledException) { break; }
            }
        }
        finally { Console.CancelKeyPress -= onCancel; }
        Console.WriteLine("  stopped watching (nothing changed).");
        return 0;
    }

    // --- lifecycle ---

    public static async Task<int> UpAsync(bool load, bool supervise = false)
    {
        Console.WriteLine(Term.Bold("\n  bringing the demo up …\n"));
        // Everything below rides on the docker engine (the ollama container, and the
        // shared-RAG guard's `docker ps`). Without it the board carries no ollama row
        // and `up` used to die on a bare LINQ "Sequence contains no matching element"
        // — refuse with the actual reason instead.
        if (Shell.Run("docker", ["info"], 8000).Code != 0)
        {
            Console.WriteLine("  " + Term.C("○ REFUSED", "31") +
                " — docker engine unreachable. Start Docker Desktop, then re-run `feedctl up`.");
            return 1;
        }
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
        // FirstOrDefault: if docker dies mid-wait the board loses its ollama row —
        // that must read as "not ok yet", never throw out of the wait loop.
        if (!await WaitAsync(async () => (await Board.GatherAsync()).FirstOrDefault(r => r.Label.StartsWith("ollama"))?.State == Term.State.Ok, 60))
            Console.WriteLine("  " + Term.C("▲ ollama slow to report healthy — continuing", "33"));

        if (!ApiHost.IsRunning())
        {
            if (ApiHost.Start() is null) return 1;
        }
        else Console.WriteLine("  " + Term.C("●", "32") + " API already running");

        Console.WriteLine("  " + Term.C("◐", "33") + " warming Poro (first /health loads the model) …");
        var healthy = await WaitAsync(async () =>
        {
            var b = await Shell.GetJsonAsync("/health", 15);
            return b is not null && b.Value.TryGetProperty("status", out var s) && s.GetString() == "ok";
        }, 120);
        if (!healthy)
        {
            // The API launched (Start() got a pid) but /health never went ok in
            // 120s — most likely it died on startup or the model won't load.
            // Report it honestly instead of exiting 0 on a demo that isn't live.
            Console.WriteLine("  " + Term.C("○ /health never reported ok (120s) — API may have died on startup; check `logs` or the board", "31"));
            Console.WriteLine(Board.Render(await Board.GatherAsync()));
            return 1;
        }

        if (load) await LoadAsync(null);
        // feedctl owns the public Funnel: bring it up so the shared (Azure) link
        // reaches this API. Symmetric with `down`, which takes it off.
        Console.WriteLine("  " + Term.C("◐", "33") + " exposing the public Funnel (the shared/Azure link) …");
        Funnel.Ensure();
        Console.WriteLine(Board.Render(await Board.GatherAsync()));
        Console.WriteLine("  open " + Term.C(Config.BaseUrl + "/", "36") + " (management view) · " +
            Term.C(Config.BaseUrl + "/desk.html", "36") + " (desk entry)");
        Console.WriteLine("  show " + Term.C(Config.PublicSiteUrl + "/demo.html", "36") + " · " +
            Term.C(Config.PublicSiteUrl + "/desk.html", "36") + " (public, via the /api proxy)");
        if (supervise) return await SuperviseAsync();
        return 0;
    }

    /// <summary>Keep the API process alive for a demo session (opt-in `up --supervise`).
    /// NOT boot-autostart — the isolation invariant forbids auto-grabbing the shared
    /// GPU/Funnel, so supervision is operator-invoked and self-limiting: each 5 s tick it
    /// re-checks the shared-RAG guard and STOPS the moment the RAG comes up (the GPU is
    /// theirs); if the API port goes dark it reaps any dead/wedged process and restarts
    /// (no rebuild), confirming with /health. A rolling-window flap guard stops it rather
    /// than respawn a crash-looping API forever. Steady-state liveness is the cheap port
    /// bind, NOT a /health poll — polling would run a Poro completion every tick, and a
    /// health shed under real report load would look like an outage and force a wrong
    /// restart; a wedged-but-still-bound API is out of scope by design. Ctrl-C stops
    /// watching but leaves the demo up.</summary>
    private static async Task<int> SuperviseAsync()
    {
        Console.WriteLine("  " + Term.C("●", "32") +
            " supervising — the API restarts if it dies (Ctrl-C stops watching; the demo stays up).");
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += onCancel;
        // Flap guard: more than a few restarts in a short window means it is crash-looping,
        // not recovering — stop rather than respawn (and leak processes) forever. Rolling,
        // so an occasional restart across a long demo never trips it.
        var restarts = new Queue<DateTime>();
        const int maxRestartsPerWindow = 5;
        var window = TimeSpan.FromMinutes(3);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                try { await Task.Delay(5000, cts.Token); }
                catch (OperationCanceledException) { break; }

                // Never contend for the shared GPU: if the RAG came up, back off entirely.
                if (Board.SharedRagUp())
                {
                    Console.WriteLine("  " + Term.C(
                        "▲ shared RAG came up — stopping supervision (the GPU is theirs). Re-run `feedctl up` when it's down.",
                        "33"));
                    break;
                }

                if (ApiHost.PortListening()) continue; // up (steady-state = port bind, see summary)

                // Flapping? Prune the rolling window, then decide before spawning again.
                var now = DateTime.UtcNow;
                while (restarts.Count > 0 && now - restarts.Peek() > window) restarts.Dequeue();
                if (restarts.Count >= maxRestartsPerWindow)
                {
                    Console.WriteLine("  " + Term.C(
                        $"○ API restarted {restarts.Count}× in {window.TotalMinutes:0} min — it is crash-looping. " +
                        "Stopping supervision; check `logs`/board.", "31"));
                    break;
                }
                restarts.Enqueue(now);

                Console.WriteLine("  " + Term.C($"▲ API not listening — restarting (#{restarts.Count} this window) …", "33"));
                ApiHost.Stop();                 // reap a dead/wedged tracked process (+ any port owner) first
                if (ApiHost.Start(build: false) is null)
                {
                    Console.WriteLine("  " + Term.C("○ could not start the API — will retry next tick.", "31"));
                    continue;
                }
                var healthy = await WaitAsync(async () =>
                {
                    var b = await Shell.GetJsonAsync("/health", 15, cts.Token);
                    return b is not null && b.Value.TryGetProperty("status", out var s) && s.GetString() == "ok";
                }, 120, cts.Token);
                if (!cts.IsCancellationRequested)
                    Console.WriteLine(healthy
                        ? "  " + Term.C("● API back up.", "32")
                        : "  " + Term.C("▲ API restarted but /health not ok yet — will re-check next tick.", "33"));
            }
        }
        finally { Console.CancelKeyPress -= onCancel; }
        Console.WriteLine("  stopped supervising (demo left up).");
        return 0;
    }

    public static Task<int> DownAsync()
    {
        Console.WriteLine(Term.Bold("\n  taking the demo down …"));
        Console.WriteLine("  " + Term.C("◐", "33") + " taking the public Funnel down (frees port 443 for the RAG) …");
        Funnel.Stop();
        Console.WriteLine("  " + Term.C("◐", "33") + " stopping API …");
        ApiHost.Stop();
        Console.WriteLine("  " + Term.C("◐", "33") + " stopping ollama (frees the GPU for the shared RAG) …");
        Shell.Run("docker", ["stop", Config.OllamaContainer], 60000);
        Console.WriteLine("  " + Term.C("●", "32") + " down — GPU free.");
        return Task.FromResult(0);
    }

    // --- data source selection: mock (AI placeholder) / demo (real) / clean ---

    /// <summary>Explicitly choose the DB's starting data: `mock` (AI-generated
    /// placeholder, non-evidential), `demo` (the real seeded corpus) or `clean`
    /// (empty). Each wipes the DB and restarts the API on it; ollama stays up.</summary>
    public static async Task<int> DataAsync(string? modeArg)
    {
        var mode = (modeArg ?? "").Trim().ToLowerInvariant();
        string? corpus = mode switch
        {
            "clean" => null,
            "mock" => Config.MockCorpus,
            "demo" => Config.RealCorpus,
            _ => "?",
        };
        if (corpus == "?")
        {
            Console.WriteLine(Term.C("  usage: data <mock|demo|clean>", "33"));
            Console.WriteLine("    " + Term.C("mock", "36") + "  — AI-generated placeholder corpus (non-evidential)");
            Console.WriteLine("    " + Term.C("demo", "36") + "  — the real seeded corpus (generated-42)");
            Console.WriteLine("    " + Term.C("clean", "36") + " — empty database, start fresh");
            return 1;
        }
        if (corpus is not null && !File.Exists(Config.Abs(corpus)))
        {
            Console.WriteLine(Term.C($"  ○ corpus not found: {corpus}", "31"));
            if (mode == "demo")
                Console.WriteLine("    (the real corpus lands on master when the real-corpus PR merges)");
            return 1;
        }

        Console.WriteLine(Term.Bold($"\n  switching dataset → {mode} …\n"));
        Console.WriteLine("  " + Term.C("◐", "33") + " wiping the databases (ollama stays up) …");
        ApiHost.Stop();
        // BOTH channels (ADR-0024): leaving desk-live.db would let a rehearsal's desk
        // entries reappear in the fresh demo's segment — the same evidential/non-
        // evidential mixing this wipe exists to prevent.
        foreach (var dbPath in new[] { Config.DemoDbPath, Config.LiveDbPath })
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(Config.Abs(dbPath) + suffix); } catch { /* best effort */ }

        Console.WriteLine("  " + Term.C("◐", "33") + " restarting the API on the empty DB …");
        if (ApiHost.Start(build: false) is null) return 1;   // no rebuild — a data switch never changes API code
        var healthy = await WaitAsync(async () =>
        {
            var b = await Shell.GetJsonAsync("/health", 15);
            return b is not null && b.Value.TryGetProperty("status", out var s) && s.GetString() == "ok";
        }, 120);
        if (!healthy)
        {
            Console.WriteLine("  " + Term.C("○ the API did not report healthy after the wipe — check `logs`/board", "31"));
            return 1;
        }

        // The DB delete above is best-effort — a stray API instance holding the file
        // could keep it alive. If the restarted API still sees rows, the wipe FAILED;
        // abort rather than load a new corpus on top of the old and mislabel the mix
        // (that would silently break the evidential/non-evidential separation).
        foreach (var probe in new[] { "/feedback?limit=1", "/live/feedback?limit=1" })
        {
            var existing = await Shell.GetJsonAsync(probe, 10);
            if (existing is { ValueKind: JsonValueKind.Array } && existing.Value.GetArrayLength() > 0)
            {
                Console.WriteLine("  " + Term.C($"○ database not empty after wipe ({probe}) — a stray API may hold the file. " +
                    "Aborting to avoid mixing datasets; run `down`, then retry.", "31"));
                return 1;
            }
        }

        // Keep the public link live across the data switch (idempotent — the API
        // port is unchanged; this just re-asserts the Funnel if it was off).
        Funnel.Ensure();

        if (corpus is null)
        {
            WriteDataset("clean");
            Console.WriteLine("  " + Term.C("●", "32") + " clean — empty database. Add entries at the desk or `load`.");
            Console.WriteLine(Board.Render(await Board.GatherAsync()));
            return 0;
        }

        var loaded = await LoadAsync(corpus);   // LoadAsync clears the marker; set the real one after
        WriteDataset(mode);
        Console.WriteLine(Board.Render(await Board.GatherAsync()));
        return loaded;
    }

    private static void WriteDataset(string mode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Config.DatasetFile)!);
            File.WriteAllText(Config.DatasetFile, mode);
        }
        catch { /* best effort — the board falls back to a bare item count */ }
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
        // Effective sentiment (ADR-0030/0031): the model's own value if it emitted
        // one, else the deterministic type→sentiment map — the same seam the report
        // uses. Labelled via /schema (Poro does not emit sentiment, so in practice
        // this shows the derived value).
        var sentiment = await EffectiveSentimentLabelAsync(s);
        Console.WriteLine($"  {Term.C("→", "32")} {s.GetProperty("category").GetString()} / " +
            $"\"{s.GetProperty("theme").GetString()}\" / {s.GetProperty("severity").GetString()} / " +
            $"{s.GetProperty("type").GetString()} / {s.GetProperty("language").GetString()}" +
            (sentiment is null ? "" : $" / {Term.C(sentiment, "35")}") +
            $"   {Term.C($"{sw.Elapsed.TotalSeconds:F1}s", "2")}{salvaged}\n");
        return 0;
    }

    /// <summary>The item's sentiment display label (ADR-0031 model value ?? ADR-0030
    /// type-derived), resolved through /schema; null if the domain declares none or
    /// /schema is unreachable.</summary>
    private static async Task<string?> EffectiveSentimentLabelAsync(JsonElement structure)
    {
        var schema = await Shell.GetJsonAsync("/schema", 15);
        if (schema is not { } sc)
            return null;
        string? key = structure.TryGetProperty("sentiment", out var mv) && mv.ValueKind == JsonValueKind.String
            ? mv.GetString()
            : null;
        if (key is null
            && structure.TryGetProperty("type", out var ty) && ty.ValueKind == JsonValueKind.String
            && sc.TryGetProperty("typeSentiment", out var ts)
            && ts.TryGetProperty(ty.GetString()!, out var d) && d.ValueKind == JsonValueKind.String)
            key = d.GetString();
        if (key is null)
            return null;
        return sc.TryGetProperty("sentimentLabels", out var sl) && sl.TryGetProperty(key, out var lv) && lv.ValueKind == JsonValueKind.String
            ? lv.GetString()
            : key;
    }

    public static async Task<int> LoadAsync(string? corpus)
    {
        // A direct load makes the board's dataset marker stale; clear it so the
        // board falls back to a bare count. `data <mode>` re-writes it afterwards.
        try { File.Delete(Config.DatasetFile); } catch { /* best effort */ }
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
        // snapshot=true: the operator's deliberate generation refreshes the offline
        // fallback (report-latest.*) that verify + publish read; ephemeral frontend
        // views do not (dotnet-audit finding #3).
        var q = $"/report?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}&snapshot=true";
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
        // Whole-window sentiment (polarity) mix (ADR-0030/0031).
        if (rep.TryGetProperty("sentimentCounts", out var sc) && sc.ValueKind == JsonValueKind.Object)
        {
            var mix = string.Join(" · ", sc.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.Number && p.Value.GetInt32() > 0)
                .Select(p => $"{p.Name} {p.Value.GetInt32()}"));
            if (mix.Length > 0)
                Console.WriteLine($"    {Term.C("◔ sentiment", "35")} {mix}");
        }
        foreach (var a in rep.GetProperty("alerts").EnumerateArray())
        {
            var reason = a.TryGetProperty("llmReason", out var lr) && lr.ValueKind == JsonValueKind.String
                ? "LLM: " + lr.GetString()
                : "keyword: " + string.Join(",", a.GetProperty("deterministicHits").EnumerateArray().Select(h => h.GetProperty("pattern").GetString()));
            Console.WriteLine($"    {Term.C("▲ ALERT", "31")} [{a.GetProperty("feedbackId").GetString()}] {reason}");
        }
        foreach (var t in rep.GetProperty("themes").EnumerateArray().Take(6))
            Console.WriteLine($"    {Term.C("●", "32")} {t.GetProperty("category").GetString(),-22} " +
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

    /// <summary>Re-run structuring over the LIVE channel with the current domain
    /// vocabulary (ADR-0026) — after adding a category, existing entries adapt to
    /// it. One LLM call per stored entry; announce GPU use as usual.</summary>
    public static async Task<int> RestructureAsync()
    {
        Console.WriteLine("  " + Term.C("◐", "33") + " re-structuring the live channel with the current categories …");
        var (status, body) = await Shell.PostJsonAsync("/live/restructure", "{}", 600);
        if (status == 503) { Console.WriteLine(Term.C("  ▲ LLM busy — try again in a moment.", "33")); return 1; }
        if (status is < 200 or >= 300 || string.IsNullOrWhiteSpace(body))
        {
            Console.WriteLine(Term.C($"  ○ restructure failed (HTTP {status}) — API up?", "31"));
            return 1;
        }
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        // TryGetProperty: alertsUpdated arrived with ADR-0027 — a still-running
        // pre-0027 API answers without it, and a missing counter must not turn
        // a SUCCEEDED server-side pass into a false CLI failure.
        var alertStamps = root.TryGetProperty("alertsUpdated", out var au) ? $"{au.GetInt32()} alert re-stamps · " : "";
        Console.WriteLine("  " + Term.C("●", "32") +
            $" {root.GetProperty("restructured").GetInt32()} restructured · " +
            $"{root.GetProperty("failed").GetInt32()} structure_failed · " +
            $"{root.GetProperty("skipped").GetInt32()} skipped (valid category, human audit kept) · " +
            alertStamps +
            $"{root.GetProperty("total").GetInt32()} total");
        return 0;
    }

    public static async Task<int> LogsAsync(int n)
    {
        // Both channels (ADR-0024): desk-UI saves live in /live/feedback — a
        // corpus-only view would read a successful desk save as "never landed".
        var limit = Math.Clamp(n, 1, 200);
        var main = await Shell.GetJsonAsync($"/feedback?limit={limit}", 15);
        if (main is null || main.Value.ValueKind != JsonValueKind.Array) { Console.WriteLine(Term.C("  ○ no data (API up?)", "31")); return 1; }
        var live = await Shell.GetJsonAsync($"/live/feedback?limit={limit}", 15);

        var items = main.Value.EnumerateArray().Select(it => (Item: it, Channel: "corpus"));
        if (live is { ValueKind: JsonValueKind.Array })
            items = items.Concat(live.Value.EnumerateArray().Select(it => (Item: it, Channel: "live")));
        var merged = items
            .OrderByDescending(x => x.Item.TryGetProperty("timestamp", out var ts) ? ts.GetString() : "", StringComparer.Ordinal)
            .Take(limit)
            .ToList();

        Console.WriteLine(Term.Bold($"\n  last {merged.Count} ingested item(s) (corpus + live):\n"));
        foreach (var (it, channel) in merged)
        {
            var failed = it.TryGetProperty("structureFailed", out var sf) && sf.GetBoolean();
            var dept = it.TryGetProperty("structure", out var st) && st.ValueKind == JsonValueKind.Object
                ? st.GetProperty("category").GetString() : (failed ? Term.C("structure_failed", "31") : "?");
            var text = it.GetProperty("text").GetString() ?? "";
            if (text.Length > 60) text = text[..60] + "…";
            var source = it.GetProperty("source").GetString() ?? "?";
            Console.WriteLine($"    {Term.C(channel, channel == "live" ? "32" : "2"),-14} {Term.C(source, "36"),-16} {dept,-22} {text}");
        }
        Console.WriteLine();
        return 0;
    }

    public static int Open()
    {
        try { using var _p = Process.Start(new ProcessStartInfo(Config.BaseUrl + "/") { UseShellExecute = true }); }
        catch (Exception ex) { Console.WriteLine(Term.C("  ○ " + ex.Message, "31")); return 1; }
        Console.WriteLine("  opened " + Term.C(Config.BaseUrl + "/", "36"));
        return 0;
    }

    private static async Task<bool> WaitAsync(Func<Task<bool>> ready, int timeoutSeconds, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (await ready()) return true;
            try { await Task.Delay(1500, ct); } catch (OperationCanceledException) { break; }
        }
        return false;
    }
}
