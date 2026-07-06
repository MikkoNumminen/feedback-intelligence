namespace FeedbackIntelligence.Llm;

/// <summary>
/// Resolves config-relative asset paths the same way everywhere: the working
/// directory first (repo checkout, tools run from the repo root), then the
/// binary's own directory (assets shipped alongside the app).
/// </summary>
public static class AppPathResolver
{
    public static string Resolve(string configured)
    {
        if (Path.IsPathRooted(configured) || File.Exists(configured))
            return configured;
        var beside = Path.Combine(AppContext.BaseDirectory, configured);
        return File.Exists(beside) ? beside : configured;
    }

    /// <summary>Reads a prompt/template file with line endings normalized to LF.
    /// This is load-bearing: an LLM's greedy decode can differ on CRLF vs LF — a
    /// Windows-checkout CRLF copy of the alert-verify prompt flipped the borderline
    /// no-keyword safety judgment (kyllä→ei), silently disabling the safety alert.
    /// Normalizing on load makes prompt behavior independent of OS, editor, and git
    /// autocrlf. Every prompt/template MUST be read through here.</summary>
    public static async Task<string> ReadPromptAsync(string configured, CancellationToken ct = default)
        => NormalizeNewlines(await File.ReadAllTextAsync(Resolve(configured), ct));

    public static string ReadPrompt(string configured)
        => NormalizeNewlines(File.ReadAllText(Resolve(configured)));

    public static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n").Replace("\r", "\n");
}
