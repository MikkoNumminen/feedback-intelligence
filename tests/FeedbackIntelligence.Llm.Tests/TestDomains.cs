using FeedbackIntelligence.Core.Domain;

namespace FeedbackIntelligence.Llm.Tests;

/// <summary>Loads the REAL retail domain descriptor from the committed module so
/// the salvage tests validate against the same taxonomy production uses.</summary>
internal static class TestDomains
{
    public static DomainDescriptor Retail() =>
        ActiveDomain.LoadDescriptor(Path.Combine(RepoRoot(), "domains", "retail", "domain.json"), "retail");

    /// <summary>Repo root by walking up to the .sln sentinel — unbounded, the
    /// repo-wide convention (reused by the red-team fixture loader).</summary>
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeedbackIntelligence.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found from test bin");
    }
}
