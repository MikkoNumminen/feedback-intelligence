using System.Globalization;
using System.Text.Json;

namespace FeedbackIntelligence.Ctl;

/// <summary>The live status board — concurrent component checks, colour-coded,
/// with a headline LIVE verdict. Includes the project's signature check: the
/// shared-RAG guard.</summary>
public static class Board
{
    public sealed record Row(string Label, Term.State State, string Detail, bool GatesLive);

    /// <summary>True iff the hands-off `mikkonumminendev` RAG stack is running.
    /// While it is, the GPU is in use and feedctl must not grab it.</summary>
    public static bool SharedRagUp()
    {
        var res = Shell.Run("docker", ["ps", "--format", "{{.Names}}"], 8000);
        return res.Code == 0 && res.Output.Contains(Config.RagStackMarker, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<List<Row>> GatherAsync()
    {
        var docker = CheckDocker();
        if (docker.State == Term.State.Down)
        {
            // Everything below depends on the engine; report once, cheaply.
            return [docker, Row2("shared RAG (hands-off)", Term.State.Warn, "unknown — docker down", false)];
        }

        // Independent checks run concurrently; the board resolves in ~max(check).
        var rag = Task.Run(CheckSharedRag);
        var ollama = Task.Run(CheckOllama);
        var model = Task.Run(CheckModel);
        var gpu = Task.Run(CheckGpu);
        var api = Task.Run(CheckApi);
        var health = CheckHealthAsync();
        var data = CheckDataAsync();
        var funnel = Task.Run(CheckFunnel);
        var snapshot = Task.Run(CheckSnapshot);

        await Task.WhenAll(rag, ollama, model, gpu, api, health, data, funnel, snapshot);
        return
        [
            docker,
            rag.Result, ollama.Result, model.Result, gpu.Result,
            api.Result, await health, await data, funnel.Result, snapshot.Result,
        ];
    }

    public static bool IsLive(IEnumerable<Row> rows) => rows.Where(r => r.GatesLive).All(r => r.State == Term.State.Ok);

    public static string Render(List<Row> rows)
    {
        var live = IsLive(rows);
        var head = live ? Term.C("demo is LIVE", "32") : Term.C("demo is not fully up", "33");
        var body = string.Join("\n", rows.Select(r => Term.Line(r.Label, r.State, r.Detail)));
        return $"\n  {Term.Bold("feedctl — " + head)}\n\n{body}\n";
    }

    // --- individual checks (State + detail) ---

    private static Row Row2(string label, Term.State s, string d, bool gates) => new(label, s, d, gates);

    private static Row CheckDocker()
    {
        var res = Shell.Run("docker", ["info"], 8000);
        return res.Code == 0
            ? Row2("docker engine", Term.State.Ok, "running", true)
            : Row2("docker engine", Term.State.Down, "unreachable — start Docker Desktop", true);
    }

    private static Row CheckSharedRag()
    {
        // A mode, not a fault of our stack: "up" is a yellow warning because it
        // blocks GPU use, but it never flips the LIVE verdict for our stack.
        return SharedRagUp()
            ? Row2("shared RAG (hands-off)", Term.State.Warn, "UP — do NOT use the GPU; run its owner's shutdown first", false)
            : Row2("shared RAG (hands-off)", Term.State.Ok, "down — GPU is free", false);
    }

    private static Row CheckOllama()
    {
        var res = Shell.Run("docker", ["ps", "--format", "{{.Names}}\t{{.Status}}"], 8000);
        var line = res.Output.Split('\n').FirstOrDefault(l => l.StartsWith(Config.OllamaContainer + "\t"));
        if (line is null)
            return Row2("ollama (isolated)", Term.State.Down, "not running", true);
        var status = line.Split('\t', 2)[1].Trim();
        var low = status.ToLowerInvariant();
        var state = low.Contains("healthy") || low.StartsWith("up") ? Term.State.Ok
            : low.Contains("starting") ? Term.State.Busy : Term.State.Warn;
        return Row2("ollama (isolated)", state, status, true);
    }

    private static Row CheckModel()
    {
        var res = Shell.Run("docker", ["exec", Config.OllamaContainer, "ollama", "ps"], 12000);
        if (res.Code != 0)
            return Row2("model loaded", Term.State.Down, "ollama not up", false);
        var rows = res.Output.Split('\n').Where(l => l.Trim().Length > 0 && !l.StartsWith("NAME")).ToList();
        if (rows.Count == 0)
            return Row2("model loaded", Term.State.Warn, "none resident (cold — warms on first call)", false);
        var names = rows.Select(r => r.Split(' ', 2)[0]).ToList();
        return Row2("model loaded", Term.State.Ok, string.Join(" · ", names), false);
    }

    private static Row CheckGpu()
    {
        var res = Shell.Run("docker", ["exec", Config.OllamaContainer, "nvidia-smi",
            "--query-gpu=utilization.gpu,memory.used,memory.total", "--format=csv,noheader,nounits"], 10000);
        var row = res.Output.Trim().Split('\n').FirstOrDefault()?.Trim() ?? "";
        var parts = row.Split(',').Select(p => p.Trim()).ToArray();
        if (res.Code != 0 || parts.Length < 3
            || !double.TryParse(parts[1], CultureInfo.InvariantCulture, out var used)
            || !double.TryParse(parts[2], CultureInfo.InvariantCulture, out var total) || total == 0)
            return Row2("GPU", Term.State.Warn, "nvidia-smi unavailable", false);
        var pct = used / total * 100;
        var detail = $"{parts[0]}% util · {parts[1]}/{parts[2]} MiB VRAM ({pct:F0}%)";
        return Row2("GPU", pct >= 95 ? Term.State.Warn : Term.State.Ok, detail, false);
    }

    private static Row CheckApi()
    {
        if (ApiHost.RunningPid() is int pid)
            return Row2("API process", Term.State.Ok, $"pid {pid} · :{Config.ApiPort}", true);
        if (ApiHost.PortListening())
            return Row2("API process", Term.State.Ok, $":{Config.ApiPort} (started outside feedctl)", true);
        return Row2("API process", Term.State.Down, "not running — `up`", true);
    }

    private static async Task<Row> CheckHealthAsync()
    {
        var body = await Shell.GetJsonAsync("/health", 12);
        if (body is null)
            return Row2("API /health", Term.State.Down, "not answering", true);
        var status = body.Value.TryGetProperty("status", out var s) ? s.GetString() : null;
        return status == "ok"
            ? Row2("API /health", Term.State.Ok, "llm ready (1-token completion)", true)
            : Row2("API /health", Term.State.Warn, $"{status ?? "?"} (warming?)", true);
    }

    private static async Task<Row> CheckDataAsync()
    {
        var body = await Shell.GetJsonAsync("/feedback?limit=1000", 10);
        if (body is null || body.Value.ValueKind != JsonValueKind.Array)
            return Row2("demo data", Term.State.Warn, "none — `data mock|demo` or `load`", false);
        var n = body.Value.GetArrayLength();
        // The desk's own channel (ADR-0024) — counted separately so a desk-only
        // session never reads as "empty" while the segment is showing entries.
        var live = await Shell.GetJsonAsync("/live/feedback?limit=1000", 10);
        var liveN = live is { ValueKind: JsonValueKind.Array } ? live.Value.GetArrayLength() : 0;
        var liveSuffix = liveN > 0 ? $" · desk live: {liveN}" : "";
        if (n == 0 && liveN == 0)
            return Row2("demo data", Term.State.Warn, "empty — `data mock|demo` or `load`", false);
        var tag = DatasetTag();
        return Row2("demo data", Term.State.Ok,
            (tag is null ? $"{n} item(s)" : $"{n} item(s) · {tag}") + liveSuffix, false);
    }

    /// <summary>Provenance of the loaded dataset, recorded by `data <mode>`; null
    /// when unknown (a direct `load` clears it, so the board shows a bare count).</summary>
    private static string? DatasetTag()
    {
        try
        {
            if (!File.Exists(Config.DatasetFile)) return null;
            return File.ReadAllText(Config.DatasetFile).Trim().ToLowerInvariant() switch
            {
                "mock" => "mock (AI placeholder · non-evidential)",
                "demo" => "demo (real seeded corpus)",
                _ => null,
            };
        }
        catch { return null; /* best effort — provenance tag is cosmetic; unreadable dataset file reads as "unknown" */ }
    }

    private static Row CheckSnapshot() =>
        File.Exists(Config.Abs(Config.SnapshotJson))
            ? Row2("report snapshot", Term.State.Ok, "present (backend-down fallback ready)", false)
            : Row2("report snapshot", Term.State.Warn, "none yet — `report`", false);

    /// <summary>The public Tailscale Funnel (the shared/Azure link's backend).
    /// Warns if it is off, or if port 443 points at a DIFFERENT target than this
    /// API — that means the sibling RAG holds it.</summary>
    private static Row CheckFunnel()
    {
        var s = Funnel.Status();
        if (!s.On)
            return Row2("public link", Term.State.Warn, "off — `up` exposes it", false);
        if (s.TargetPort is int t && t != Config.ApiPort)
            return Row2("public link", Term.State.Warn, $"443 -> :{t} (NOT this API - the RAG?)", false);
        return Row2("public link", Term.State.Ok, s.Url ?? "on", false);
    }
}
