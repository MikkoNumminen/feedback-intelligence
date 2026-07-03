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

        var template = await File.ReadAllTextAsync(ResolvePath(opts.VariantsPromptPath), ct);
        var core = CorpusItem.LoadJsonl(opts.CorePath);
        Console.WriteLine($"Multiplying {core.Count} core items × {opts.VariantsPerItem} variants each.");

        var chatOptions = new ChatOptions
        {
            Temperature = opts.VariantsTemperature,
            MaxOutputTokens = opts.VariantsMaxOutputTokens > 0 ? opts.VariantsMaxOutputTokens : null,
        };

        var variants = new List<CorpusItem>();
        var failed = 0;
        foreach (var item in core)
        {
            var prompt = template
                .Replace("{{count}}", opts.VariantsPerItem.ToString(), StringComparison.Ordinal)
                .Replace("{{text}}", item.Text, StringComparison.Ordinal);

            var texts = await RequestVariantsAsync(prompt, chatOptions, ct)
                ?? await RequestVariantsAsync(prompt, chatOptions, ct); // one retry
            if (texts is null)
            {
                failed++;
                Console.Error.WriteLine($"  {item.Id}: no valid variants after retry — skipped.");
                continue;
            }

            var n = 0;
            foreach (var text in texts.Distinct(StringComparer.Ordinal))
                variants.Add(new CorpusItem($"{item.Id}-v{++n}", item.Source, text, Story: item.Story, SourceId: item.Id));
            // The original itself joins the pool — hand-written texts are the
            // best material and must be findable in generated sets too.
            variants.Add(new CorpusItem($"{item.Id}-v0", item.Source, item.Text, Story: item.Story, SourceId: item.Id));
            Console.WriteLine($"  {item.Id}: {n} variants");
        }

        await CorpusItem.SaveJsonlAsync(opts.VariantsPath, variants, ct);
        Console.WriteLine($"\nWrote {variants.Count} pool items to {Path.GetFullPath(opts.VariantsPath)} ({failed} core items skipped).");
        Console.WriteLine("Commit this file — generate composes ONLY from the committed pool.");
        return failed == 0 ? 0 : 1;
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

    private static string ResolvePath(string configured)
    {
        if (Path.IsPathRooted(configured) || File.Exists(configured))
            return configured;
        var beside = Path.Combine(AppContext.BaseDirectory, configured);
        return File.Exists(beside) ? beside : configured;
    }
}
