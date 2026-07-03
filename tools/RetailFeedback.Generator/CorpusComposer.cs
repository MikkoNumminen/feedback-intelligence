namespace RetailFeedback.Generator;

/// <summary>
/// Pure, seeded corpus composition — deliberately free of IO, clocks, and LLM
/// clients so the generate path can NEVER involve a live model (Phase 1
/// confirmation) and the same seed always produces the same corpus. All
/// randomness flows from the single seeded Random in a fixed call order.
/// </summary>
public static class CorpusComposer
{
    private sealed record Draft(string Text, string Source, string Timestamp, string? StoryId);

    public static (List<CorpusItem> Items, GroundTruthFile GroundTruth) Compose(
        IReadOnlyList<CorpusItem> pool,
        GeneratorOptions options,
        int seed,
        bool nonEvidential)
    {
        var rng = new Random(seed);
        var anchor = DateOnly.ParseExact(options.AnchorDate, "yyyy-MM-dd");

        var noisePool = pool.Where(i => string.IsNullOrEmpty(i.Story)).ToList();
        var drafts = new List<Draft>();
        var storyWindows = new List<(StoryConfig Story, DateOnly From, DateOnly To)>();

        foreach (var story in options.Stories)
        {
            var storyPool = pool.Where(i => i.Story == story.Id).ToList();
            if (storyPool.Count == 0)
                throw new InvalidDataException(
                    $"No pool items tagged story '{story.Id}' — the planted story cannot be composed. " +
                    "Tag core items with this story id and re-run the variants step.");

            var sequenced = storyPool.Where(i => i.Sequence.HasValue).ToList();
            var unsequenced = storyPool.Where(i => !i.Sequence.HasValue).ToList();
            if (sequenced.Count > 0 && unsequenced.Count > 0)
                throw new InvalidDataException(
                    $"Story '{story.Id}': pool mixes sequenced and unsequenced items — tag all or none.");

            var from = anchor.AddDays(-(story.WindowDays - 1));
            storyWindows.Add((story, from, anchor));

            drafts.AddRange(sequenced.Count > 0
                ? ComposeSequencedStory(story, sequenced, from, anchor, rng, options)
                : ComposeSpreadStory(story, unsequenced, from, rng, options));
        }

        if (options.NoiseCount > 0 && noisePool.Count == 0)
            throw new InvalidDataException("No untagged pool items available for base noise.");
        var noiseShuffled = noisePool.Count > 0 ? Shuffle(noisePool, rng) : [];
        var noiseFrom = anchor.AddDays(-(options.NoiseWindowDays - 1));
        string[] allSources = ["google_review", "email", "web_form", "desk"];
        for (var n = 0; n < options.NoiseCount; n++)
        {
            var item = noiseShuffled[n % noiseShuffled.Count];
            var dt = RandomTime(noiseFrom.AddDays(rng.Next(0, options.NoiseWindowDays)), rng, options);
            drafts.Add(new Draft(
                item.Text,
                item.Source ?? allSources[rng.Next(allSources.Length)],
                Stamp(dt),
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

    /// <summary>
    /// Sequence-preserving arc (Mikko, 2026-07-03): the trend must be visible in
    /// CONTENT, so timestamps are strictly monotonic with the authored sequence —
    /// a "third time already" complaint must never precede the first mild one.
    /// One realization per step (original or a variant, seed-varied); config
    /// Count does not apply — the step count does.
    /// </summary>
    private static List<Draft> ComposeSequencedStory(
        StoryConfig story,
        List<CorpusItem> sequenced,
        DateOnly from,
        DateOnly anchor,
        Random rng,
        GeneratorOptions options)
    {
        var steps = sequenced
            .GroupBy(i => i.Sequence!.Value)
            .OrderBy(g => g.Key)
            .ToList();

        // The validator can only check MinGroundedIds against config Count; for
        // sequenced pools the effective count is the step count, known only here.
        if (story.MinGroundedIds > steps.Count)
            throw new InvalidDataException(
                $"Story '{story.Id}': minGroundedIds {story.MinGroundedIds} exceeds the {steps.Count} authored " +
                "sequence steps — the ground truth would be unsatisfiable. Lower minGroundedIds or write more steps.");

        var windowEnd = anchor.AddDays(1).ToDateTime(TimeOnly.MinValue); // exclusive
        var drafts = new List<Draft>(steps.Count);
        var prev = DateTime.MinValue;
        for (var i = 0; i < steps.Count; i++)
        {
            var candidates = steps[i].ToList();
            var item = candidates[rng.Next(candidates.Count)];
            var fraction = steps.Count == 1 ? 1.0 : (double)i / (steps.Count - 1);
            if (story.Trend == "worsening")
                fraction = Math.Sqrt(fraction); // shrinking gaps => density rises toward the window end
            var day = (int)Math.Round(fraction * (story.WindowDays - 1));
            var dt = RandomTime(from.AddDays(day), rng, options);
            if (dt <= prev)
                dt = prev.AddMinutes(rng.Next(options.SequenceCollisionGapMinMinutes, options.SequenceCollisionGapMaxMinutes));
            if (dt >= windowEnd)
                throw new InvalidDataException(
                    $"Story '{story.Id}': {steps.Count} sequence steps do not fit strictly monotonic inside " +
                    $"WindowDays={story.WindowDays} — widen the window or reduce steps.");
            prev = dt;
            drafts.Add(new Draft(item.Text, PickSource(story, rng), Stamp(dt), story.Id));
        }
        return drafts;
    }

    /// <summary>Unsequenced story: Count items spread over the window; worsening =
    /// frequency escalation (~1/3 in the first half, the rest in the second).</summary>
    private static List<Draft> ComposeSpreadStory(
        StoryConfig story,
        List<CorpusItem> pool,
        DateOnly from,
        Random rng,
        GeneratorOptions options)
    {
        var shuffled = Shuffle(pool, rng);
        var drafts = new List<Draft>(story.Count);
        for (var n = 0; n < story.Count; n++)
        {
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
            var dt = RandomTime(from.AddDays(dayOffset), rng, options);
            drafts.Add(new Draft(item.Text, PickSource(story, rng), Stamp(dt), story.Id));
        }
        return drafts;
    }

    private static string PickSource(StoryConfig story, Random rng) =>
        story.Sources[rng.Next(story.Sources.Count)];

    /// <summary>Single point of truth for the time-of-day policy — story and noise
    /// items must share one hour distribution, or an hour histogram leaks story
    /// membership past the id shuffle.</summary>
    private static DateTime RandomTime(DateOnly day, Random rng, GeneratorOptions options) =>
        day.ToDateTime(new TimeOnly(rng.Next(options.DayStartHour, options.DayEndHour), rng.Next(0, 60)));

    // InvariantCulture is load-bearing: on a fi-FI machine the ':' custom
    // format specifier renders as '.', producing invalid ISO timestamps.
    private static string Stamp(DateTime dt) =>
        dt.ToString("yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture) + ":00+03:00";

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
