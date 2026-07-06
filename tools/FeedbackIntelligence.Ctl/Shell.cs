using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FeedbackIntelligence.Ctl;

/// <summary>Subprocess + local-HTTP helpers. feedctl is an orchestrator (like
/// ragctl): it shells out to docker/dotnet and talks to the local API over
/// HTTP, rather than reaching into internals.</summary>
public static class Shell
{
    public sealed record RunResult(int Code, string Output);

    public static RunResult Run(string file, IEnumerable<string> args, int timeoutMs = 30000, string? cwd = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = cwd ?? Config.RepoRoot,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi);
            if (p is null)
                return new RunResult(127, "");
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return new RunResult(124, "");
            }
            // The process exited. Normally the stream reads complete immediately, but
            // a detached grandchild that inherited this stdout pipe could hold it open —
            // making ReadToEnd().Result block forever. Bound the post-exit wait and use
            // whatever was captured. (The API itself is now launched via ShellExecuteEx,
            // which inherits no handles, so it never triggers this; this is a general guard.)
            Task.WhenAll(stdout, stderr).Wait(2000);
            var output = (stdout.IsCompletedSuccessfully ? stdout.Result : "")
                + (stderr.IsCompletedSuccessfully ? stderr.Result : "");
            return new RunResult(p.ExitCode, output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new RunResult(127, ""); // executable not found
        }
    }

    // Infinite base timeout: each call governs its own deadline via the linked
    // CTS below (the report's live synthesis can take 20s+, well past a default).
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task<JsonElement?> GetJsonAsync(string path, int timeoutSeconds = 10, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            var doc = await Http.GetFromJsonAsync<JsonElement>(Config.BaseUrl + path, cts.Token);
            return doc;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<(int Status, string Body)> PostJsonAsync(string path, string json, int timeoutSeconds = 120, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(Config.BaseUrl + path, content, cts.Token);
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(cts.Token));
        }
        catch (Exception ex)
        {
            return (0, ex.Message);
        }
    }
}
