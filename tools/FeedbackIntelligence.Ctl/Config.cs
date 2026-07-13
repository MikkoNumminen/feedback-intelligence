namespace FeedbackIntelligence.Ctl;

/// <summary>Constants (env-overridable), ragctl-style. Kept in one place so the
/// operator surface is easy to see and retune.</summary>
public static class Config
{
    public static readonly string RepoRoot = FindRepoRoot();

    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("FEEDCTL_BASE_URL") ?? "http://localhost:5088";

    public const int ApiPort = 5088;

    /// <summary>This project's isolated Ollama container (never the shared RAG's).</summary>
    public const string OllamaContainer = "retail-rag-ollama";

    /// <summary>Substring that identifies the live, hands-off RAG stack in `docker ps`.
    /// If any container name contains this, the GPU is in use and feedctl must not grab it.</summary>
    public const string RagStackMarker = "mikkonumminendev";

    public const string ApiProject = "src/FeedbackIntelligence.Api";
    public const string GeneratorProject = "tools/FeedbackIntelligence.Generator";
    public const string DemoDbPath = "data/demo.db";

    /// <summary>The desk's own live-channel database (ADR-0024). ctl passes it to
    /// the API explicitly — without that the API would resolve its relative default
    /// against its own CWD and the file would land where no ctl command manages it.</summary>
    public const string LiveDbPath = "data/desk-live.db";

    public const string SnapshotJson = "data/snapshots/report-latest.json";

    /// <summary>The public demo site (Azure SWA). Its /api managed function is the
    /// same-origin proxy every browser uses to reach the backend (ADR-0025) — the
    /// board probes it so "works on my curl" can never mask a dead public path.</summary>
    public const string PublicSiteUrl = "https://red-ground-0bacf9c03.7.azurestaticapps.net";

    /// <summary>The real, evidential seeded corpus (`data` demo mode).</summary>
    public const string RealCorpus = "data/corpus/generated-42.jsonl";

    /// <summary>The AI-generated placeholder corpus (`data` mock mode) — NON-EVIDENTIAL.</summary>
    public const string MockCorpus = "data/corpus/generated-placeholder-42.jsonl";

    public static string PidFile => Path.Combine(RepoRoot, ".feedctl", "api.pid");

    /// <summary>Records which dataset feedctl last loaded (mock/demo/clean) so the
    /// board can show provenance. Best-effort: a direct `load` clears it.</summary>
    public static string DatasetFile => Path.Combine(RepoRoot, ".feedctl", "dataset");

    public static string Abs(string relative) => Path.GetFullPath(Path.Combine(RepoRoot, relative));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeedbackIntelligence.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
