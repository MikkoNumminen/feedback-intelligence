using System.Text.Json;
using FeedbackIntelligence.Generator;

namespace FeedbackIntelligence.Generator.Tests;

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
                Category = "maito_kylma",
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
                Category = "rakennustarvike",
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
                Category = "leipa",
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
            Assert.Equal(config.Category, story.ExpectedCategory);
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
        Assert.All(items, i => Assert.Null(i.Sequence));
    }

    private static GeneratorOptions MakeSequencedDairyOptions() => new()
    {
        AnchorDate = "2026-07-01",
        NoiseCount = 0,
        Stories =
        [
            new StoryConfig
            {
                Id = "dairy-freshness-worsening",
                Kind = "recurring_signal",
                Category = "maito_kylma",
                ThemeKeywords = ["tuoreus"],
                Sources = ["desk", "email"],
                WindowDays = 14,
                Count = 9, // ignored for sequenced pools — one item per step
                Trend = "worsening",
                MinGroundedIds = 3,
                ExpectAlert = false,
            },
        ],
    };

    private static List<CorpusItem> MakeSequencedDairyPool()
    {
        var pool = new List<CorpusItem>();
        for (var step = 1; step <= 5; step++)
            for (var v = 0; v < 3; v++)
                pool.Add(new CorpusItem(
                    $"sd-{step}-v{v}", "desk", $"arc-step-{step} muotoilu {v}",
                    Story: "dairy-freshness-worsening", Sequence: step));
        return pool;
    }

    [Fact]
    public void SequencedStory_TimestampsStrictlyMonotonicWithSequence()
    {
        var (items, truth) = CorpusComposer.Compose(MakeSequencedDairyPool(), MakeSequencedDairyOptions(), 42, false);
        var byId = items.ToDictionary(i => i.Id);
        var dairy = truth.Stories.Single();

        // One realization per authored step, Count config ignored.
        Assert.Equal(5, dairy.FeedbackIds.Count);

        var inTimeOrder = dairy.FeedbackIds
            .Select(id => byId[id])
            .OrderBy(i => DateTimeOffset.Parse(i.Timestamp!))
            .ToList();

        // A "third time already" complaint must never precede the first mild
        // one: content order (arc-step-N) must equal time order, strictly.
        var steps = inTimeOrder
            .Select(i => int.Parse(i.Text.Split("arc-step-")[1].Split(' ')[0]))
            .ToList();
        Assert.Equal([1, 2, 3, 4, 5], steps);

        var stamps = inTimeOrder.Select(i => DateTimeOffset.Parse(i.Timestamp!)).ToList();
        for (var i = 1; i < stamps.Count; i++)
            Assert.True(stamps[i] > stamps[i - 1], "timestamps must be strictly increasing with sequence");

        // Monotonicity may never buy itself room beyond the window: every stamp
        // stays within [windowFrom, windowTo].
        var windowFrom = DateOnly.Parse(dairy.WindowFrom);
        var windowTo = DateOnly.Parse(dairy.WindowTo);
        Assert.All(stamps, s => Assert.InRange(DateOnly.FromDateTime(s.DateTime), windowFrom, windowTo));
    }

    [Fact]
    public void SequencedStory_TooManyStepsForWindow_FailsLoudly()
    {
        var options = MakeSequencedDairyOptions();
        options.Stories[0] = new StoryConfig
        {
            Id = "dairy-freshness-worsening",
            Kind = "recurring_signal",
            Category = "maito_kylma",
            ThemeKeywords = ["tuoreus"],
            Sources = ["desk"],
            WindowDays = 1,
            Count = 1,
            Trend = "worsening",
            MinGroundedIds = 1,
            ExpectAlert = false,
        };
        var pool = new List<CorpusItem>();
        for (var step = 1; step <= 40; step++)
            pool.Add(new CorpusItem($"sd-{step}", "desk", $"arc-step-{step}", Story: "dairy-freshness-worsening", Sequence: step));

        var ex = Assert.Throws<InvalidDataException>(() => CorpusComposer.Compose(pool, options, 42, false));
        Assert.Contains("do not fit", ex.Message);
    }

    [Fact]
    public void SequencedStory_MinGroundedIdsExceedingSteps_FailsLoudly()
    {
        var options = MakeSequencedDairyOptions();
        var pool = new List<CorpusItem>
        {
            new("sd-1", "desk", "arc-step-1", Story: "dairy-freshness-worsening", Sequence: 1),
            new("sd-2", "desk", "arc-step-2", Story: "dairy-freshness-worsening", Sequence: 2),
        };

        // Options demand MinGroundedIds=3 but only 2 steps exist — an
        // unsatisfiable ground truth must fail at compose time, not in Phase 4.
        var ex = Assert.Throws<InvalidDataException>(() => CorpusComposer.Compose(pool, options, 42, false));
        Assert.Contains("unsatisfiable", ex.Message);
    }

    [Fact]
    public void SequencedStory_VariesRealizationAcrossSeeds()
    {
        var pool = MakeSequencedDairyPool();
        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var (items, truth) = CorpusComposer.Compose(pool, MakeSequencedDairyOptions(), seed, false);
            var byId = items.ToDictionary(i => i.Id);
            texts.Add(byId[truth.Stories.Single().FeedbackIds[0]].Text);
        }

        // Different seeds should not all pick the same variant for a step.
        Assert.True(texts.Count > 1, "seed must vary which variant realizes a sequence step");
    }

    [Fact]
    public void MixedSequencedAndUnsequencedStoryPool_FailsLoudly()
    {
        var pool = MakeSequencedDairyPool();
        pool.Add(new CorpusItem("sd-x", "desk", "sekalainen ilman järjestystä", Story: "dairy-freshness-worsening"));

        var ex = Assert.Throws<InvalidDataException>(() =>
            CorpusComposer.Compose(pool, MakeSequencedDairyOptions(), 42, false));
        Assert.Contains("tag all or none", ex.Message);
    }

    [Fact]
    public void VariantItems_InheritStoryTagAndSequence()
    {
        var core = new CorpusItem("core-007", "desk", "maito hapanta KOLMAS kerta", Story: "dairy-freshness-worsening", Sequence: 5);

        var items = VariantsRunner.ToVariantItems(core, ["eri asiakkaan muotoilu", "toinen muotoilu"]);

        Assert.Equal(3, items.Count); // 2 variants + the original as v0
        Assert.Contains(items, i => i.Id == "core-007-v0" && i.Text == core.Text);
        Assert.All(items, i => Assert.Equal("dairy-freshness-worsening", i.Story));
        Assert.All(items, i => Assert.Equal(5, i.Sequence));
        Assert.All(items, i => Assert.Equal("core-007", i.SourceId));
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
