using FeedbackIntelligence.Core.Domain;
using FeedbackIntelligence.Generator;

namespace FeedbackIntelligence.Generator.Tests;

/// <summary>StoryLibrary validates each story against the ACTIVE domain — its
/// category and (this PR) its sources — so `generate` can never compose an item
/// the same domain's ingest would reject.</summary>
public class StoryLibraryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"stories-{Guid.NewGuid():N}.json");

    private static readonly DomainDescriptor Retail =
        ActiveDomain.LoadDescriptor(Path.Combine(RepoRoot(), "domains", "retail", "domain.json"), "retail");

    public void Dispose() => File.Delete(_path);

    private static string OneStory(string source) =>
        $$"""[{"Id":"s1","Kind":"recurring_signal","Category":"maito_kylma","ThemeKeywords":["x"],"Sources":["{{source}}"],"WindowDays":7,"Count":3,"Trend":"stable","MinGroundedIds":1,"ExpectAlert":false}]""";

    [Fact]
    public void ForeignStorySource_IsRejectedAtLoad()
    {
        File.WriteAllText(_path, OneStory("steam_review")); // a game channel, foreign to retail
        var ex = Assert.Throws<InvalidDataException>(() => StoryLibrary.Load(_path, Retail));
        Assert.Contains("steam_review", ex.Message);
        Assert.Contains("domain source", ex.Message);
    }

    [Fact]
    public void InDomainStorySource_Loads()
    {
        File.WriteAllText(_path, OneStory("desk")); // a retail channel
        var stories = StoryLibrary.Load(_path, Retail);
        Assert.Single(stories);
        Assert.Equal("desk", stories[0].Sources.Single());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeedbackIntelligence.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found from test bin");
    }
}
