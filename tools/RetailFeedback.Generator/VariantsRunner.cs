using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RetailFeedback.Llm;
using RetailFeedback.Llm.Structuring;

namespace RetailFeedback.Generator;

/// <summary>
/// OFFLINE LLM multiplication of the core corpus (announced GPU window, output
/// committed) — the only LLM-touching verb in the generator. The analyzer must
/// meet generated data cold, so this never runs at demo/generate time.
/// </summary>
public sealed class VariantsRunner(
    [FromKeyedServices(LlmServiceCollectionExtensions.SynthesisKey)] IChatClient chatClient,
    IOptions<GeneratorOptions> options)
{
    public async Task<int> RunAsync(bool force, CancellationToken ct)
    {
        var opts = options.Value;
        if (!File.Exists(opts.CorePath))
        {
            Console.Error.WriteLine($"Core corpus not found: {Path.GetFullPath(opts.CorePath)} — the hand-written corpus must exist first.");
            return 1;
        }
        if (File.Exists(opts.VariantsPath) && !force)
        {
            Console.Error.WriteLine(
                $"{opts.VariantsPath} already exists — it is a committed artifact. Pass --force to regenerate deliberately.");
            return 1;
        }

        // Templates load lazily: StoryVariantsPerItem=0 means "no LLM call for
        // story items" and must not demand the story prompt file exist.
        string? noiseTemplate = null;
        string? storyTemplate = null;
        var core = CorpusItem.LoadJsonl(opts.CorePath);
        Console.WriteLine(
            $"Multiplying {core.Count} core items (noise ×{opts.VariantsPerItem}, story ×{opts.StoryVariantsPerItem}).");

        var chatOptions = new ChatOptions
        {
            Temperature = opts.VariantsTemperature,
            MaxOutputTokens = opts.VariantsMaxOutputTokens > 0 ? opts.VariantsMaxOutputTokens : null,
        };

        var variants = new List<CorpusItem>();
        var failed = 0;
        foreach (var item in core)
        {
            var isStory = !string.IsNullOrEmpty(item.Story);
            var perItem = isStory ? opts.StoryVariantsPerItem : opts.VariantsPerItem;
            if (perItem == 0)
            {
                variants.AddRange(ToVariantItems(item, []));
                Console.WriteLine($"  {item.Id}: originals only (×0)");
                continue;
            }

            // Story items get a dedicated intensity-preserving prompt (arc
            // protection, Mikko 2026-07-03): a mild rewording of a severe
            // "third time already" text corrupts the authored escalation.
            var template = isStory
                ? storyTemplate ??= await File.ReadAllTextAsync(ResolvePath(opts.VariantsStoryPromptPath), ct)
                : noiseTemplate ??= await File.ReadAllTextAsync(ResolvePath(opts.VariantsPromptPath), ct);
            var prompt = template
                .Replace("{{count}}", perItem.ToString(), StringComparison.Ordinal)
                .Replace("{{text}}", item.Text, StringComparison.Ordinal);

            var texts = await RequestVariantsAsync(prompt, chatOptions, ct)
                ?? await RequestVariantsAsync(prompt, chatOptions, ct); // one retry
            if (texts is null)
            {
                failed++;
                Console.Error.WriteLine($"  {item.Id}: no valid variants after retry — skipped.");
                continue;
            }

            var made = ToVariantItems(item, texts);
            variants.AddRange(made);
            Console.WriteLine($"  {item.Id}: {made.Count - 1} variants");
        }

        await CorpusItem.SaveJsonlAsync(opts.VariantsPath, variants, ct);
        Console.WriteLine($"\nWrote {variants.Count} pool items to {Path.GetFullPath(opts.VariantsPath)} ({failed} core items skipped).");
        Console.WriteLine("Commit this file — generate composes ONLY from the committed pool.");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>Variants inherit story tag AND sequence — an arc step's variant
    /// is the same step told by a different customer. The original always joins
    /// the pool as v0.</summary>
    public static List<CorpusItem> ToVariantItems(CorpusItem core, IReadOnlyList<string> variantTexts)
    {
        var items = new List<CorpusItem>();
        var n = 0;
        foreach (var text in variantTexts.Distinct(StringComparer.Ordinal))
            items.Add(new CorpusItem($"{core.Id}-v{++n}", core.Source, text, Story: core.Story, SourceId: core.Id, Sequence: core.Sequence));
        items.Add(new CorpusItem($"{core.Id}-v0", core.Source, core.Text, Story: core.Story, SourceId: core.Id, Sequence: core.Sequence));
        return items;
    }

    private async Task<List<string>?> RequestVariantsAsync(string prompt, ChatOptions chatOptions, CancellationToken ct)
    {
        string raw;
        try
        {
            raw = (await chatClient.GetResponseAsync(prompt, chatOptions, ct)).Text;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  LLM call failed: {ex.Message}");
            return null;
        }

        if (!LlmJsonExtractor.TryExtractObject(raw, out var doc, out _))
            return null;
        using (doc)
        {
            if (!doc!.RootElement.TryGetProperty("variants", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return null;
            var texts = arr.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .ToList();
            return texts.Count > 0 ? texts : null;
        }
    }

    private static string ResolvePath(string configured) => AppPathResolver.Resolve(configured);
}
