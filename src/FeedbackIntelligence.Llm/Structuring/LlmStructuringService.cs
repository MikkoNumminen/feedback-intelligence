using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FeedbackIntelligence.Core.Domain;
using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Llm.Structuring;

public sealed class LlmStructuringService(
    [FromKeyedServices(LlmServiceCollectionExtensions.StructuringKey)] IChatClient chatClient,
    IOptions<StructuringOptions> options,
    IActiveDomain activeDomain,
    ILogger<LlmStructuringService> logger) : IStructuringService
{
    private string? _template;

    public async Task<StructuringResult> StructureAsync(string feedbackText, CancellationToken ct = default)
    {
        var template = await LoadTemplateAsync(ct);
        var prompt = template.Replace("{{text}}", feedbackText, StringComparison.Ordinal);
        var chatOptions = new ChatOptions
        {
            Temperature = options.Value.Temperature,
            MaxOutputTokens = options.Value.MaxOutputTokens > 0 ? options.Value.MaxOutputTokens : null,
        };

        var raw = (await chatClient.GetResponseAsync(prompt, chatOptions, ct)).Text;
        var attempt = StructuringOutputParser.Parse(raw, activeDomain.Descriptor);
        if (attempt.Structure is not null)
        {
            LogNotes(attempt);
            return new StructuringResult(attempt.Structure, raw, attempt.Salvaged, attempt.Normalized, Retried: false, attempt.Notes);
        }

        logger.LogWarning(
            "Structuring output violated the schema, re-prompting once. Violations: {Violations}",
            string.Join("; ", attempt.Violations));

        var retryPrompt = prompt
            + "\n\nYour previous reply was invalid: "
            + string.Join("; ", attempt.Violations)
            + "\nReturn ONLY the corrected single JSON object, nothing else.";

        raw = (await chatClient.GetResponseAsync(retryPrompt, chatOptions, ct)).Text;
        var retry = StructuringOutputParser.Parse(raw, activeDomain.Descriptor);
        if (retry.Structure is not null)
        {
            LogNotes(retry);
            return new StructuringResult(retry.Structure, raw, retry.Salvaged, retry.Normalized, Retried: true, retry.Notes);
        }

        logger.LogError(
            "Structuring failed after one retry; storing structure_failed with raw output preserved. Retry violations: {Violations}",
            string.Join("; ", retry.Violations));

        var notes = new List<string>();
        notes.AddRange(attempt.Violations.Select(v => "first attempt: " + v));
        notes.AddRange(retry.Violations.Select(v => "retry: " + v));
        notes.AddRange(retry.Notes);
        return new StructuringResult(null, raw, retry.Salvaged, Normalized: false, Retried: true, notes);
    }

    private void LogNotes(StructuringOutputParser.Attempt attempt)
    {
        foreach (var note in attempt.Notes)
            logger.LogInformation("Structuring salvage note: {Note}", note);
    }

    private async Task<string> LoadTemplateAsync(CancellationToken ct)
    {
        if (_template is not null)
            return _template;

        var path = AppPathResolver.Resolve(options.Value.PromptPath);
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"Structuring prompt not found: '{options.Value.PromptPath}' (cwd: {Environment.CurrentDirectory}).");

        // Normalize CRLF→LF: an LLM's greedy decode can differ on line endings
        // (a CRLF alert-verify prompt silently disabled the safety alert — see
        // AppPathResolver.ReadPromptAsync), so structuring must be immune too.
        var template = AppPathResolver.NormalizeNewlines(await File.ReadAllTextAsync(path, ct));
        if (!template.Contains("{{text}}", StringComparison.Ordinal))
            throw new InvalidOperationException($"Structuring prompt '{path}' must contain the {{{{text}}}} placeholder.");
        if (!template.Contains("{{categories}}", StringComparison.Ordinal))
            logger.LogWarning(
                "Structuring prompt '{Path}' has no {{{{categories}}}} placeholder — the model gets no taxonomy "
                + "list, so it may emit out-of-domain category values the salvage layer then rejects.", path);

        // Fill the domain-taxonomy placeholders once — they are constant per
        // active domain. {{text}} stays for per-call substitution. A neutral
        // prompt names no categories itself; the active domain supplies them.
        var d = activeDomain.Descriptor;
        template = template
            .Replace("{{categories}}", RenderLabelledKeys(d.CategoryLabels), StringComparison.Ordinal)
            .Replace("{{severities}}", RenderJsonArray(d.SeverityLabels.Keys), StringComparison.Ordinal)
            .Replace("{{types}}", RenderJsonArray(d.TypeLabels.Keys), StringComparison.Ordinal);

        return _template = template;
    }

    private static string RenderJsonArray(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.Select(v => JsonSerializer.Serialize(v))) + "]";

    /// <summary>Render each category as <c>"key" (Label)</c> — one per line — so
    /// the model has the human meaning of every department, not just an opaque
    /// enum key. Bare keys made Poro misread "runkopuu/lankku" as liha_kala; the
    /// Finnish label ("Rakennustarvikkeet") is the disambiguating signal. The
    /// quoted key is still the exact value to return; the parenthetical is a hint.
    /// Domain-neutral: labels come from the active domain, so a new domain (e.g.
    /// game) supplies its own without touching this code.</summary>
    private static string RenderLabelledKeys(IReadOnlyDictionary<string, string> labels) =>
        string.Join("\n  ", labels.Select(kv => $"{JsonSerializer.Serialize(kv.Key)} ({kv.Value})"));
}
