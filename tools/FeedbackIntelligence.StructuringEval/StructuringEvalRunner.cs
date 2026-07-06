using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using FeedbackIntelligence.Core.Domain;
using FeedbackIntelligence.Llm;

namespace FeedbackIntelligence.StructuringEval;

public sealed record EvalRecord(
    string Model,
    string ItemId,
    int Rep,
    long LatencyMs,
    string Raw,
    ValidatedOutput Validated);

public sealed class StructuringEvalRunner(
    ILlmClientFactory clientFactory,
    IOptions<LlmOptions> llmOptions,
    IOptions<EvalOptions> evalOptions,
    IActiveDomain activeDomain)
{
    private static readonly JsonSerializerOptions RawJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The Phase 0 spike: one completion from every configured model, through the abstraction.</summary>
    public async Task<int> PingAsync(CancellationToken ct)
    {
        var models = evalOptions.Value.Candidates
            .Append(llmOptions.Value.Models.Structuring)
            .Append(llmOptions.Value.Models.Synthesis)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.Ordinal);

        var ok = true;
        foreach (var model in models)
        {
            using var client = clientFactory.CreateForModel(model);
            var sw = Stopwatch.StartNew();
            try
            {
                var response = await client.GetResponseAsync("Reply with exactly: pong", cancellationToken: ct);
                sw.Stop();
                Console.WriteLine($"OK   {model,-55} {sw.ElapsedMilliseconds,6} ms  \"{Truncate(response.Text.Trim(), 60)}\"");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                ok = false;
                Console.WriteLine($"FAIL {model,-55} {sw.ElapsedMilliseconds,6} ms  {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (!ok)
            Console.WriteLine("\nIs the project Ollama up? Start it with: docker compose up -d ollama (GPU is shared — coordinate first).");
        return ok ? 0 : 1;
    }

    public async Task<int> EvalAsync(CancellationToken ct)
    {
        var eval = evalOptions.Value;

        var promptPath = ResolvePromptPath(eval.PromptPath);
        if (!File.Exists(promptPath))
        {
            Console.Error.WriteLine($"Prompt file not found: {promptPath}");
            return 1;
        }
        // Normalize CRLF→LF (ADR-0018): line endings can skew an LLM's output, so
        // a comparison harness must not silently vary with the prompt's checkout.
        var promptTemplate = FeedbackIntelligence.Llm.AppPathResolver.NormalizeNewlines(await File.ReadAllTextAsync(promptPath, ct));
        if (!promptTemplate.Contains("{{text}}", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Prompt file {promptPath} must contain the {{{{text}}}} placeholder.");
            return 1;
        }

        // Fill the domain-taxonomy placeholders once (same neutral prompt the API
        // uses); {{text}} stays for the per-item substitution below.
        var descriptor = activeDomain.Descriptor;
        promptTemplate = promptTemplate
            .Replace("{{categories}}", RenderJsonArray(descriptor.CategoryLabels.Keys), StringComparison.Ordinal)
            .Replace("{{severities}}", RenderJsonArray(descriptor.SeverityLabels.Keys), StringComparison.Ordinal)
            .Replace("{{types}}", RenderJsonArray(descriptor.TypeLabels.Keys), StringComparison.Ordinal);

        if (!File.Exists(eval.InputPath))
        {
            Console.Error.WriteLine($"Input file not found: {Path.GetFullPath(eval.InputPath)} — run from the repo root.");
            return 1;
        }
        var items = EvalInput.LoadJsonl(eval.InputPath);
        Console.WriteLine($"Loaded {items.Count} input items from {eval.InputPath}.");
        if (items.Count < 10)
            Console.WriteLine($"WARNING: only {items.Count} item(s) — too few to discriminate between models. The hand-written corpus is the real fixture.");

        var records = new List<EvalRecord>();
        foreach (var model in eval.Candidates)
        {
            Console.WriteLine($"\n=== {model} ===  ('.' adherent, '!' parse/schema violation, 'X' unparseable)");
            // API-level reasoning suppression, applied identically to every
            // candidate (a no-op for non-reasoning models) so arms stay fair.
            // The /no_think prompt soft switch is NOT honored on Ollama's native
            // chat path — measured in the 2026-07-03 placeholder run, where
            // thinking silently consumed the whole output-token budget.
            using var client = clientFactory.CreateForModel(model, eval.DisableThinking);
            var chatOptions = new ChatOptions { Temperature = eval.Temperature };
            if (eval.MaxOutputTokens > 0)
                chatOptions.MaxOutputTokens = eval.MaxOutputTokens;

            foreach (var item in items)
            {
                for (var rep = 1; rep <= eval.Repetitions; rep++)
                {
                    var prompt = promptTemplate.Replace("{{text}}", item.Text, StringComparison.Ordinal);
                    var sw = Stopwatch.StartNew();
                    string raw;
                    try
                    {
                        var response = await client.GetResponseAsync(prompt, chatOptions, ct);
                        raw = response.Text;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"\nCall failed ({model}, item {item.Id}, rep {rep}): {ex.Message}");
                        Console.Error.WriteLine("Aborting — partial results are not written; rerun when Ollama is reachable.");
                        return 1;
                    }
                    sw.Stop();

                    var validated = OutputValidation.Validate(raw, descriptor);
                    records.Add(new EvalRecord(model, item.Id, rep, sw.ElapsedMilliseconds, raw, validated));
                    Console.Write(validated switch
                    {
                        { SchemaAdherent: true } => ".",
                        { Outcome: ParseOutcome.Unparseable } => "X",
                        _ => "!",
                    });
                }
            }
            Console.WriteLine();
        }

        Directory.CreateDirectory(eval.OutputDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var reportPath = Path.Combine(eval.OutputDir, $"structuring-eval-{stamp}.md");
        var rawPath = Path.Combine(eval.OutputDir, $"structuring-eval-{stamp}.raw.json");

        await File.WriteAllTextAsync(reportPath, MarkdownReport.Render(eval, items, records, promptPath), ct);
        await File.WriteAllTextAsync(rawPath, JsonSerializer.Serialize(records, RawJsonOptions), ct);

        Console.WriteLine($"\nReport:      {Path.GetFullPath(reportPath)}");
        Console.WriteLine($"Raw results: {Path.GetFullPath(rawPath)}");
        Console.WriteLine();
        Console.WriteLine(MarkdownReport.RenderSummaryTable(eval, records));
        if (MarkdownReport.IsPlaceholderRun(eval))
            Console.WriteLine("NON-EVIDENTIAL: placeholder inputs — pipeline proof only, never a model decision (AGENTS.md hard rule).");
        return 0;
    }

    private static string ResolvePromptPath(string configured) => AppPathResolver.Resolve(configured);

    private static string RenderJsonArray(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.Select(v => JsonSerializer.Serialize(v))) + "]";

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
