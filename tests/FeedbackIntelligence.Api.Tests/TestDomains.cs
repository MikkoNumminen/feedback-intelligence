using FeedbackIntelligence.Core.Domain;

namespace FeedbackIntelligence.Api.Tests;

/// <summary>Loads the REAL retail domain descriptor from the committed module so
/// request/structure validation tests use the same taxonomy as production.</summary>
internal static class TestDomains
{
    public static DomainDescriptor Retail() =>
        ActiveDomain.LoadDescriptor(Path.Combine(RepoRoot(), "domains", "retail", "domain.json"), "retail");

    public static DomainDescriptor Game() =>
        ActiveDomain.LoadDescriptor(Path.Combine(RepoRoot(), "domains", "game", "domain.json"), "game");

    /// <summary>A fully-loaded retail <see cref="IActiveDomain"/> for services that
    /// take the domain by interface (paths resolve to the committed module). Pass
    /// <paramref name="promptOverride"/> to point every prompt role at a test file.</summary>
    public static IActiveDomain RetailActive(string? promptOverride = null) =>
        new StubActiveDomain(Retail(), promptOverride);

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeedbackIntelligence.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found from test bin");
    }

    private sealed class StubActiveDomain : IActiveDomain
    {
        public StubActiveDomain(DomainDescriptor descriptor, string? promptOverride)
        {
            Descriptor = descriptor;
            var dir = Path.Combine(RepoRoot(), "domains", "retail");
            AlertKeywordsPath = Path.Combine(dir, "alert-keywords.json");
            StoriesPath = Path.Combine(dir, "stories.json");
            PromptPaths = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["synthesis"] = promptOverride ?? Path.Combine(dir, "prompts", "synthesis-v0.txt"),
                ["alertNomination"] = promptOverride ?? Path.Combine(dir, "prompts", "alert-nomination-v0.txt"),
            };
        }

        public DomainDescriptor Descriptor { get; }
        public string Name => Descriptor.Name;
        public string AlertKeywordsPath { get; }
        public string StoriesPath { get; }
        public IReadOnlyDictionary<string, string> PromptPaths { get; }
        public string PromptPath(string role) => PromptPaths.TryGetValue(role, out var p)
            ? p : throw new InvalidOperationException($"no test prompt for role '{role}'");
    }
}
