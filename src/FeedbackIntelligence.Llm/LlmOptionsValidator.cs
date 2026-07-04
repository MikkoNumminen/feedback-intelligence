using Microsoft.Extensions.Options;

namespace FeedbackIntelligence.Llm;

public sealed class LlmOptionsValidator : IValidateOptions<LlmOptions>
{
    private static readonly IReadOnlySet<string> KnownProviders =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ollama" };

    public ValidateOptionsResult Validate(string? name, LlmOptions options)
    {
        var failures = new List<string>();

        if (!KnownProviders.Contains(options.Provider))
            failures.Add($"Llm:Provider must be one of [{string.Join(", ", KnownProviders)}], got '{options.Provider}'.");

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            failures.Add($"Llm:BaseUrl must be an absolute http(s) URL, got '{options.BaseUrl}'.");

        if (string.IsNullOrWhiteSpace(options.Models.Structuring))
            failures.Add("Llm:Models:Structuring must be a non-empty model name.");

        if (string.IsNullOrWhiteSpace(options.Models.Synthesis))
            failures.Add("Llm:Models:Synthesis must be a non-empty model name.");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}
