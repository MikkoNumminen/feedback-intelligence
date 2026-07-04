using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FeedbackIntelligence.Core.Structuring;

namespace FeedbackIntelligence.Llm.Structuring;

public sealed class LlmStructuringService(
    [FromKeyedServices(LlmServiceCollectionExtensions.StructuringKey)] IChatClient chatClient,
    IOptions<StructuringOptions> options,
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
        var attempt = StructuringOutputParser.Parse(raw);
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
        var retry = StructuringOutputParser.Parse(raw);
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

        var template = await File.ReadAllTextAsync(path, ct);
        if (!template.Contains("{{text}}", StringComparison.Ordinal))
            throw new InvalidOperationException($"Structuring prompt '{path}' must contain the {{{{text}}}} placeholder.");

        return _template = template;
    }
}
