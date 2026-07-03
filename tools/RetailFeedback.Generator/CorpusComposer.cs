namespace RetailFeedback.Generator;

/// <summary>
/// Pure, seeded corpus composition — deliberately free of IO, clocks, and LLM
/// clients so the generate path can NEVER involve a live model (Phase 1
/// confirmation) and the same seed always produces the same corpus. All
/// randomness flows from the single seeded Random in a fixed call order.
/// </summary>
public static class CorpusComposer
{
    public static (List<CorpusItem> Items, GroundTruthFile GroundTruth) Compose(
        IReadOnlyList<CorpusItem> pool,
        GeneratorOptions options,
        int seed,
        bool nonEvidential)
    {
        var rng = new Random(seed);
        var anchor = DateOnly.ParseExact(options.AnchorDate, "yyyy-MM-dd");

        var noisePool = pool.Where(i => string.IsNullOrEmpty(i.Story)).ToList();
        var drafts = new List<(string Text, string Source, string Timestamp, string? StoryId)>();
        var storyWindows = new List<(StoryConfig Story, DateOnly From, DateOnly To)>();

        foreach (var story in options.Stories)
        {
            var storyPool = pool.Where(i => i.Story == story.Id).ToList();
            if (storyPool.Count == 0)
                throw new InvalidDataException(
                    $"No pool items tagged story '{story.Id}' — the planted story cannot be composed. " +
                    "Tag core items with this story id and re-run the variants step.");

            var from = anchor.AddDays(-(story.WindowDays - 1));
            storyWindows.Add((story, from, anchor));
            var shuffled = Shuffle(storyPool, rng);

            for (var n = 0; n < story.Count; n++)
            {
                // Worsening = frequency escalation: ~1/3 of items land in the
                // first half of the window, the rest in the second half.
                int dayOffset;
                if (story.Trend == "worsening" && story.Count > 1)
                {
                    var firstHalf = story.WindowDays / 2;
                    var inFirstHalf = n < Math.Max(1, story.Count / 3);
                    dayOffset = inFirstHalf
                        ? rng.Next(0, Math.Max(1, firstHalf))
                        : rng.Next(firstHalf, story.WindowDays);
                }
                else
                {
                    dayOffset = rng.Next(0, story.WindowDays);
                }

                var item = shuffled[n % shuffled.Count];
                drafts.Add((
                    item.Text,
                    story.Sources[rng.Next(story.Sources.Count)],
                    Stamp(from.AddDays(dayOffset), rng),
                    story.Id));
            }
        }

        if (options.NoiseCount > 0 && noisePool.Count == 0)
            throw new InvalidDataException("No untagged pool items available for base noise.");
        var noiseShuffled = noisePool.Count > 0 ? Shuffle(noisePool, rng) : [];
        var noiseFrom = anchor.AddDays(-(options.NoiseWindowDays - 1));
        string[] allSources = ["google_review", "email", "web_form", "desk"];
        for (var n = 0; n < options.NoiseCount; n++)
        {
            var item = noiseShuffled[n % noiseShuffled.Count];
            drafts.Add((
                item.Text,
                item.Source ?? allSources[rng.Next(allSources.Length)],
                Stamp(noiseFrom.AddDays(rng.Next(0, options.NoiseWindowDays)), rng),
                null));
        }

        // Shuffle the combined set, THEN assign ids — story membership is only
        // recoverable through the ground-truth file, never from the corpus.
        var order = Shuffle(drafts, rng);
        var items = new List<CorpusItem>(order.Count);
        var idsByStory = options.Stories.ToDictionary(s => s.Id, _ => new List<string>(), StringComparer.Ordinal);
        for (var i = 0; i < order.Count; i++)
        {
            var id = $"gen-{seed}-{i:D4}";
            var draft = order[i];
            items.Add(new CorpusItem(id, draft.Source, draft.Text, draft.Timestamp));
            if (draft.StoryId is not null)
                idsByStory[draft.StoryId].Add(id);
        }

        var stories = storyWindows
            .Select(w => new GroundTruthStory(
                w.Story.Id,
                w.Story.Kind,
                idsByStory[w.Story.Id],
                w.Story.Department,
                w.Story.ThemeKeywords,
                w.From.ToString("yyyy-MM-dd"),
                w.To.ToString("yyyy-MM-dd"),
                w.Story.Trend,
                w.Story.MinGroundedIds,
                w.Story.ExpectAlert))
            .ToList();

        return (items, new GroundTruthFile(seed, options.AnchorDate, nonEvidential, stories));
    }

    private static string Stamp(DateOnly date, Random rng) =>
        $"{date:yyyy-MM-dd}T{rng.Next(8, 22):D2}:{rng.Next(0, 60):D2}:00+03:00";

    private static List<T> Shuffle<T>(IReadOnlyList<T> source, Random rng)
    {
        var list = source.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
