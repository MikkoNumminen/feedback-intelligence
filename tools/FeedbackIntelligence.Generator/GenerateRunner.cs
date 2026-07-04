using System.Text.Json;
using Microsoft.Extensions.Options;
using FeedbackIntelligence.Core.Domain;

namespace FeedbackIntelligence.Generator;

/// <summary>File IO around <see cref="CorpusComposer"/>. Contains NO LLM
/// dependency by design — generate only ever composes from the committed
/// variants file.</summary>
public sealed class GenerateRunner(IOptions<GeneratorOptions> options, IActiveDomain activeDomain)
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<int> RunAsync(int seed, CancellationToken ct)
    {
        var opts = options.Value;

        // Stories and ingest channels are domain data — load/validate stories,
        // and take the channel list from the active domain (the noise-source
        // fallback must never invent a channel foreign to the domain).
        opts.Stories = StoryLibrary.Load(activeDomain.StoriesPath, activeDomain.Descriptor);
        opts.Sources = activeDomain.Descriptor.Sources.ToList();
        Console.WriteLine($"Loaded {opts.Stories.Count} planted stories from domain '{activeDomain.Name}'.");

        if (!File.Exists(opts.VariantsPath))
        {
            Console.Error.WriteLine(
                $"Variants file not found: {Path.GetFullPath(opts.VariantsPath)}. " +
                "Run the 'variants' verb first (announced GPU window) and commit its output.");
            return 1;
        }

        var pool = CorpusItem.LoadJsonl(opts.VariantsPath);
        Console.WriteLine($"Loaded {pool.Count} pool items from {opts.VariantsPath}.");

        // Same non-evidential discipline as Phase 0, enforced by filename:
        // dev-placeholder pools mark every derived artifact.
        var nonEvidential = opts.VariantsPath.Contains("placeholder", StringComparison.OrdinalIgnoreCase);
        var suffix = nonEvidential ? $"placeholder-{seed}" : seed.ToString();

        var (items, groundTruth) = CorpusComposer.Compose(pool, opts, seed, nonEvidential);

        Directory.CreateDirectory(opts.OutputDir);
        var corpusPath = Path.Combine(opts.OutputDir, $"generated-{suffix}.jsonl");
        var truthPath = Path.Combine(opts.OutputDir, $"ground-truth-{suffix}.json");
        await CorpusItem.SaveJsonlAsync(corpusPath, items, ct);
        await File.WriteAllTextAsync(truthPath, JsonSerializer.Serialize(groundTruth, Pretty), ct);

        Console.WriteLine($"Corpus:       {Path.GetFullPath(corpusPath)}  ({items.Count} items)");
        Console.WriteLine($"Ground truth: {Path.GetFullPath(truthPath)}  ({groundTruth.Stories.Count} planted stories)");
        foreach (var story in groundTruth.Stories)
            Console.WriteLine($"  {story.Id}: {story.FeedbackIds.Count} items, {story.WindowFrom}..{story.WindowTo}, {story.Trend}");
        if (nonEvidential)
            Console.WriteLine("NON-EVIDENTIAL: composed from placeholder pool — dev machinery exercise only; nothing derived from this appears in any demo or report (AGENTS.md hard rule).");
        return 0;
    }
}
