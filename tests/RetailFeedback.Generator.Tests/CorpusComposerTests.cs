using System.Text.Json;
using RetailFeedback.Domain.Structuring;
using RetailFeedback.Generator;

namespace RetailFeedback.Generator.Tests;

/// <summary>
/// Acceptance-aligned tests for the seeded composer: same seed → identical
/// corpus; different seeds → different surface, same findable stories; ground
/// truth machine-checkable; no story-tag leakage into the generated output.
/// Pure — no files, no LLM, no clock.
/// </summary>
public class CorpusComposerTests
{
    private static GeneratorOptions MakeOptions() => new()
    {
        AnchorDate = "2026-07-01",
        NoiseCount = 20,
        NoiseWindowDays = 21,
        Stories =
        [
            new StoryConfig
            {
                Id = "dairy-freshness-worsening",
                Kind = "recurring_signal",
                Department = "maito_kylma",
                ThemeKeywords = ["tuoreus", "vanhentunut"],
                Sources = ["google_review", "email", "web_form", "desk"],
                WindowDays = 14,
                Count = 9,
                Trend = "worsening",
                MinGroundedIds = 4,
                ExpectAlert = false,
            },
            new StoryConfig
            {
                Id = "safety-no-keyword",
                Kind = "alert_by_understanding",
                Department = "rakennustarvike",
                ThemeKeywords = ["turvallisuus"],
                Sources = ["web_form"],
                WindowDays = 7,
                Count = 1,
                Trend = "stable",
                MinGroundedIds = 1,
                ExpectAlert = true,
            },
            new StoryConfig
            {
                Id = "availability-slow-burn",
                Kind = "recurring_signal",
                Department = "leipa",
                ThemeKeywords = ["saatavuus", "loppu"],
                Sources = ["desk", "google_review"],
                WindowDays = 21,
                Count = 7,
                Trend = "worsening",
                MinGroundedIds = 3,
                ExpectAlert = false,
            },
        ],
    };

    private static List<CorpusItem> MakePool()
    {
        var pool = new List<CorpusItem>();
        for (var i = 0; i < 5; i++)
            pool.Add(new CorpusItem($"d-{i}", "desk", $"maito vanhaa {i}", Story: "dairy-freshness-worsening"));
        for (var i = 0; i < 2; i++)
            pool.Add(new CorpusItem($"s-{i}", "web_form", $"lauta petti {i}", Story: "safety-no-keyword"));
        for (var i = 0; i < 4; i++)
            pool.Add(new CorpusItem($"a-{i}", "desk", $"leipä loppu {i}", Story: "availability-slow-burn"));
        for (var i = 0; i < 10; i++)
            pool.Add(new CorpusItem($"n-{i}", "email", $"yleistä palautetta {i}"));
        return pool;
    }

    [Fact]
    public void SameSeed_ProducesIdenticalOutput()
    {
        var (items1, truth1) = CorpusComposer.Compose(MakePool(), MakeOptions(), 42, false);
        var (items2, truth2) = CorpusComposer.Compose(MakePool(), MakeOptions(), 42, false);

        Assert.Equal(JsonSerializer.Serialize(items1), JsonSerializer.Serialize(items2));
        Assert.Equal(JsonSerializer.Serialize(truth1), JsonSerializer.Serialize(truth2));
    }

    [Fact]
    public void DifferentSeeds_DifferentSurface_SameStoryStructure()
    {
        var (items1, truth1) = CorpusComposer.Compose(MakePool(), MakeOptions(), 42, false);
        var (items2, truth2) = CorpusComposer.Compose(MakePool(), MakeOptions(), 43, false);

        Assert.NotEqual(JsonSerializer.Serialize(items1), JsonSerializer.Serialize(items2));
        Assert.Equal(truth1.Stories.Count, truth2.Stories.Count);
        foreach (var (s1, s2) in truth1.Stories.Zip(truth2.Stories))
        {
            Assert.Equal(s1.Id, s2.Id);
            Assert.Equal(s1.FeedbackIds.Count, s2.FeedbackIds.Count);
        }
    }

    [Fact]
    public void GroundTruth_IsMachineCheckable()
    {
        var options = MakeOptions();
        var (items, truth) = CorpusComposer.Compose(MakePool(), options, 42, false);
        var byId = items.ToDictionary(i => i.Id);

        Assert.Equal(options.Stories.Count, truth.Stories.Count);
        foreach (var story in truth.Stories)
        {
            var config = options.Stories.Single(s => s.Id == story.Id);
            Assert.Equal(config.Count, story.FeedbackIds.Count);
            Assert.True(story.MinGroundedIds >= 1 && story.MinGroundedIds <= story.FeedbackIds.Count);
            Assert.Contains(story.ExpectedDepartment, StructuringSchema.Departments);
            Assert.NotEmpty(story.ExpectedThemeKeywords);

            var from = DateOnly.Parse(story.WindowFrom);
            var to = DateOnly.Parse(story.WindowTo);
            foreach (var id in story.FeedbackIds)
            {
                Assert.True(byId.ContainsKey(id), $"ground-truth id {id} missing from corpus");
                var date = DateOnly.FromDateTime(DateTimeOffset.Parse(byId[id].Timestamp!).DateTime);
                Assert.InRange(date, from, to);
            }
        }
    }

    [Fact]
    public void WorseningStory_EscalatesFrequencyTowardWindowEnd()
    {
        var (items, truth) = CorpusComposer.Compose(MakePool(), MakeOptions(), 42, false);
        var byId = items.ToDictionary(i => i.Id);
        var dairy = truth.Stories.Single(s => s.Id == "dairy-freshness-worsening");

        var from = DateOnly.Parse(dairy.WindowFrom);
        var to = DateOnly.Parse(dairy.WindowTo);
        var midpoint = from.AddDays((to.DayNumber - from.DayNumber) / 2);
        var later = dairy.FeedbackIds.Count(id =>
            DateOnly.FromDateTime(DateTimeOffset.Parse(byId[id].Timestamp!).DateTime) >= midpoint);

        Assert.True(later > dairy.FeedbackIds.Count - later, "worsening story must concentrate in the later half of its window");
    }

    [Fact]
    public void GeneratedItems_NeverLeakStoryTags()
    {
        var (items, _) = CorpusComposer.Compose(MakePool(), MakeOptions(), 42, false);

        Assert.All(items, i => Assert.Null(i.Story));
        Assert.All(items, i => Assert.Null(i.SourceId));
    }

    [Fact]
    public void NonEvidentialFlag_PropagatesToGroundTruth()
    {
        var (_, truth) = CorpusComposer.Compose(MakePool(), MakeOptions(), 42, nonEvidential: true);

        Assert.True(truth.NonEvidential);
    }

    [Fact]
    public void MissingStoryPool_FailsLoudly()
    {
        var poolWithoutSafety = MakePool().Where(i => i.Story != "safety-no-keyword").ToList();

        var ex = Assert.Throws<InvalidDataException>(() =>
            CorpusComposer.Compose(poolWithoutSafety, MakeOptions(), 42, false));
        Assert.Contains("safety-no-keyword", ex.Message);
    }
}
