using System.Globalization;
using System.Text;

namespace FeedbackIntelligence.Ctl;

/// <summary>feedctl — operator CLI for the feedback demo. One tool that owns the
/// "get the demo live" lifecycle, a live status board (with the shared-RAG
/// guard), and the demo verbs. Modelled on mikkonumminen.dev's ragctl.</summary>
internal static class Program
{
    private static readonly (string Cmd, string Desc)[] Menu =
    [
        ("status", "live status board (one-shot)"),
        ("watch", "live board, refreshing (Ctrl-C exits)"),
        ("up [--load]", "ollama + API + public Funnel — refuses if the shared RAG is up"),
        ("down", "stop Funnel + API + ollama (frees the GPU + port 443)"),
        ("data <mode>", "choose dataset: mock | demo | clean (wipes + loads)"),
        ("demo [--seed N]", "generate → load → report → verify (full run-through)"),
        ("interpret \"text\"", "live desk structuring of one sentence, timed"),
        ("load [--corpus P]", "push a corpus through POST /feedback"),
        ("report [--days N]", "generate a report and summarize it"),
        ("verify [--ground-truth P]", "acceptance vs the ground-truth file"),
        ("telemetry [--days N]", "per-field desk correction rates"),
        ("logs [-n N]", "recent ingested items"),
        ("open", "open the management view in a browser"),
        ("exit", "leave feedctl"),
    ];

    private static async Task<int> Main(string[] args)
    {
        // No args on a TTY → interactive REPL. Piped/scripted no-args prints help.
        if (args.Length == 0 && !Console.IsInputRedirected)
            return await Repl();
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }
        return await Dispatch(args);
    }

    private static async Task<int> Dispatch(string[] a)
    {
        var cmd = a[0].ToLowerInvariant();
        var rest = a.Skip(1).ToArray();
        try
        {
            return cmd switch
            {
                "status" => await Commands.StatusAsync(),
                "watch" => await Commands.WatchAsync(),
                "up" => await Commands.UpAsync(load: HasFlag(rest, "--load")),
                "down" => await Commands.DownAsync(),
                "data" => await Commands.DataAsync(rest.FirstOrDefault()),
                "demo" => await Commands.DemoAsync(IntOpt(rest, "--seed", 42)),
                "interpret" => await Commands.InterpretAsync(FreeText(rest)),
                "load" => await Commands.LoadAsync(StrOpt(rest, "--corpus")),
                "report" => await Commands.ReportAsync(IntOpt(rest, "--days", 30)),
                "verify" => await Commands.VerifyAsync(StrOpt(rest, "--ground-truth")),
                "telemetry" => await Commands.TelemetryAsync(IntOpt(rest, "--days", 30)),
                "logs" => await Commands.LogsAsync(IntOpt(rest, "-n", 20)),
                "open" => Commands.Open(),
                "help" or "-h" or "--help" => PrintHelp(),
                _ => Unknown(cmd),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine(Term.C("  ✗ " + ex.Message, "31"));
            return 1;
        }
    }

    private static async Task<int> Repl()
    {
        Console.WriteLine(Term.Bold("\n  feedctl") + " — operator console for the feedback demo.");
        PrintMenu();
        while (true)
        {
            Console.Write(Term.C("feedctl>", "36") + " ");
            var line = Console.ReadLine();
            if (line is null) { Console.WriteLine(); break; } // Ctrl-D / EOF
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line is "exit" or "quit") break;
            var argv = Tokenize(line);
            try { await Dispatch(argv); }
            catch (Exception ex) { Console.WriteLine(Term.C("  ✗ " + ex.Message, "31")); }
            PrintMenu();
        }
        Console.WriteLine("  bye.");
        return 0;
    }

    private static void PrintMenu()
    {
        Console.WriteLine();
        Console.WriteLine(Term.Bold("  commands") + Term.C("   ·  'exit' to leave", "2"));
        foreach (var (c, d) in Menu)
            Console.WriteLine($"    {c,-28}{Term.C(d, "2")}");
    }

    private static int PrintHelp()
    {
        Console.WriteLine("\n  feedctl <command> [options]   (no command on a terminal → interactive console)\n");
        foreach (var (c, d) in Menu.Where(m => m.Cmd != "exit"))
            Console.WriteLine($"    {c,-28}{d}");
        Console.WriteLine();
        return 0;
    }

    private static int Unknown(string cmd)
    {
        Console.WriteLine(Term.C($"  unknown command '{cmd}'", "33"));
        PrintHelp();
        return 1;
    }

    // --- tiny arg helpers ---

    private static bool HasFlag(string[] a, string flag) =>
        a.Any(x => x.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string? StrOpt(string[] a, string key)
    {
        var i = Array.FindIndex(a, x => x.Equals(key, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    private static int IntOpt(string[] a, string key, int fallback) =>
        StrOpt(a, key) is { } s && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>The non-option remainder joined — for `interpret "some text"`.</summary>
    private static string FreeText(string[] a) =>
        string.Join(" ", a.Where(x => !x.StartsWith('-')));

    /// <summary>Split a REPL line into argv, honoring double-quoted spans.</summary>
    private static string[] Tokenize(string line)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        foreach (var ch in line)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (char.IsWhiteSpace(ch) && !inQuote)
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(ch);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens.ToArray();
    }
}
