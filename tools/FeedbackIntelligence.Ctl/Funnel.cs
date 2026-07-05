using System.Text.RegularExpressions;

namespace FeedbackIntelligence.Ctl;

/// <summary>The public Tailscale Funnel that exposes the local API to the shared
/// (Azure) link. feedctl OWNS it: `up`/`data` bring it up, `down` takes it off —
/// so a take-turns switch with the sibling RAG (ragctl funnels its OWN :8000 on
/// the same public port 443) always leaves a consistent state, and the 443
/// collision is surfaced instead of being silently overridden.</summary>
public static class Funnel
{
    public sealed record State(bool On, int? TargetPort, string? Url);

    /// <summary>Parse `tailscale funnel status`: whether a funnel is on, the local
    /// target port it proxies to, and the public URL.</summary>
    public static State Status()
    {
        var res = Shell.Run("tailscale", ["funnel", "status"], 10000);
        if (res.Code != 0 || !res.Output.Contains("Funnel on", StringComparison.OrdinalIgnoreCase))
            return new State(false, null, null);
        var portMatch = Regex.Match(res.Output, @"127\.0\.0\.1:(\d+)");
        int? target = portMatch.Success && int.TryParse(portMatch.Groups[1].Value, out var p) ? p : null;
        var url = res.Output.Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("https://", StringComparison.Ordinal))
            ?.Split(' ')[0];
        return new State(true, target, url);
    }

    /// <summary>Expose the API on the public Funnel (idempotent). Warns — never
    /// silently overrides — if 443 currently points at a DIFFERENT target, which
    /// means the sibling RAG holds it and `ragctl down` should run first.</summary>
    public static void Ensure()
    {
        var s = Status();
        if (s.On && s.TargetPort is int t && t != Config.ApiPort)
            Console.WriteLine("  " + Term.C(
                $"▲ public port 443 was funneled to :{t} (the RAG backend?) — taking it for this demo. Run `ragctl down` first next time.", "33"));
        var res = Shell.Run("tailscale", ["funnel", "--bg", Config.ApiPort.ToString()], 20000);
        if (res.Code != 0)
            Console.WriteLine("  " + Term.C("○ could not start the Funnel (is Tailscale up? is Funnel enabled for the tailnet?)", "31"));
    }

    /// <summary>Take the public Funnel down, freeing port 443 for the sibling RAG.</summary>
    public static void Stop() => Shell.Run("tailscale", ["funnel", "--https=443", "off"], 20000);

    public static string? Url() => Status().Url;
}
