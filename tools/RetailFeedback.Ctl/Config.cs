namespace RetailFeedback.Ctl;

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

    public const string ApiProject = "src/RetailFeedback.Api";
    public const string GeneratorProject = "tools/RetailFeedback.Generator";
    public const string DemoDbPath = "data/demo.db";
    public const string SnapshotJson = "data/snapshots/report-latest.json";

    public static string PidFile => Path.Combine(RepoRoot, ".feedctl", "api.pid");

    public static string Abs(string relative) => Path.GetFullPath(Path.Combine(RepoRoot, relative));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RetailFeedback.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}
