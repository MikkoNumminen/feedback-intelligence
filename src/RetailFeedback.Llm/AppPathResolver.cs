namespace RetailFeedback.Llm;

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
}
