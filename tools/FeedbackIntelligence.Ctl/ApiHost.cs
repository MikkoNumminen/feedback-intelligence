using System.Diagnostics;

namespace FeedbackIntelligence.Ctl;

/// <summary>
/// Lifecycle of the .NET API process. Unlike ragctl's dockerized backend, our
/// API is a plain `dotnet` process, so feedctl tracks it by PID file and starts
/// it detached (logs to .feedctl/api.log) so it outlives the feedctl invocation.
/// Launch goes through PowerShell Start-Process — the reliable way to detach +
/// redirect on this Windows host.
/// </summary>
public static class ApiHost
{
    public static string LogFile => Path.Combine(Config.RepoRoot, ".feedctl", "api.log");

    public static int? RunningPid()
    {
        try
        {
            if (!File.Exists(Config.PidFile))
                return null;
            if (!int.TryParse(File.ReadAllText(Config.PidFile).Trim(), out var pid))
                return null;
            var p = Process.GetProcessById(pid); // throws if not running
            return p.HasExited ? null : pid;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsRunning() => RunningPid() is not null;

    /// <summary>Something is serving on the API port — true even if feedctl didn't
    /// start it (e.g. a manual `dotnet run`), so the board never claims "down"
    /// while the API is actually up.</summary>
    public static bool PortListening()
    {
        try
        {
            using var c = new System.Net.Sockets.TcpClient();
            return c.ConnectAsync("127.0.0.1", Config.ApiPort).Wait(500) && c.Connected;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Builds (optionally) the API, then starts it detached on the demo
    /// DB. Returns the pid, or null on failure. <paramref name="build"/> is false
    /// for a data switch, which never changes API code: rebuilding there is wasted
    /// work and can HANG if a stray API instance still holds the DLL.</summary>
    public static int? Start(bool build = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Config.PidFile)!);

        if (build)
        {
            Console.WriteLine("  " + Term.C("◐", "33") + " building the API …");
            var buildResult = Shell.Run("dotnet", ["build", Config.Abs(Config.ApiProject), "--nologo", "-v", "quiet"], 300000);
            if (buildResult.Code != 0)
            {
                Console.WriteLine("  " + Term.C("○ build failed", "31"));
                Console.WriteLine(buildResult.Output);
                return null;
            }
        }

        var dll = Path.Combine(Config.Abs(Config.ApiProject), "bin", "Debug", "net8.0", "FeedbackIntelligence.Api.dll");
        if (!File.Exists(dll))
        {
            Console.WriteLine("  " + Term.C("○ the API is not built — run `up` first.", "31"));
            return null;
        }
        var apiDir = Config.Abs(Config.ApiProject);
        var db = Config.Abs(Config.DemoDbPath);
        var snapDir = Config.Abs("data/snapshots");
        // Start-Process detaches, redirects logs to a file, and -PassThru gives
        // us the PID to track. Working dir = the API PROJECT dir (NOT the repo
        // root): ASP.NET's ContentRoot defaults to the CWD, so it must be where
        // appsettings.json and wwwroot live — exactly what `dotnet run --project`
        // does. (Repo-root CWD left the Llm config empty and crashed startup.)
        // domains/ and prompts/ resolve via the binary's own dir (copied at
        // build); DB + snapshot are absolute so they are CWD-independent.
        // PowerShell single-quoted strings escape an apostrophe by doubling it;
        // a path like C:\Users\O'Brien would otherwise close the string (parse
        // error, or worse a command-injection surface). Escape every value
        // interpolated into the single-quoted script below.
        static string Ps(string s) => s.Replace("'", "''");
        var argList = string.Join(",",
            $"'{Ps(dll)}'", "'--urls'", $"'{Ps(Config.BaseUrl)}'",
            "'--Ingest:DbPath=" + Ps(db) + "'", "'--Report:SnapshotDir=" + Ps(snapDir) + "'");
        // Launch detached and have the launcher WRITE the pid to a file. We do NOT
        // read the pid from Shell.Run's stdout: the detached API can inherit and hold
        // that stdout pipe open, which would make the read block (the root cause of
        // `up`/`data` hangs). A file is the reliable channel; Shell.Run itself now
        // bounds its post-exit stream wait so it always returns.
        var launchPidFile = Config.Abs(Path.Combine(".feedctl", "api.launch.pid"));
        try { File.Delete(launchPidFile); } catch { /* best effort */ }
        var script =
            $"$proc = Start-Process dotnet -ArgumentList {argList} -WorkingDirectory '{Ps(apiDir)}' " +
            $"-WindowStyle Hidden -RedirectStandardOutput '{Ps(LogFile)}' -RedirectStandardError '{Ps(LogFile)}.err' -PassThru; " +
            $"Set-Content -Path '{Ps(launchPidFile)}' -Value $proc.Id -Encoding ascii -NoNewline";
        Shell.Run("powershell", ["-NoProfile", "-Command", script], 120000);

        var pid = 0;
        for (var attempt = 0; attempt < 30 && pid == 0; attempt++)
        {
            try
            {
                if (File.Exists(launchPidFile) && int.TryParse(File.ReadAllText(launchPidFile).Trim(), out var parsed))
                    pid = parsed;
            }
            catch { /* file may be mid-write — retry */ }
            if (pid == 0) System.Threading.Thread.Sleep(100);
        }
        if (pid == 0)
        {
            Console.WriteLine("  " + Term.C("○ could not start the API process", "31"));
            return null;
        }
        File.WriteAllText(Config.PidFile, pid.ToString());
        return pid;
    }

    public static void Stop()
    {
        var pid = RunningPid();
        if (pid is int p)
            Shell.Run("taskkill", ["/PID", p.ToString(), "/T", "/F"], 20000);
        // Fallback: kill anything still listening on the API port.
        Shell.Run("powershell", ["-NoProfile", "-Command",
            $"$c=(Get-NetTCPConnection -LocalPort {Config.ApiPort} -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1).OwningProcess; if ($c) {{ Stop-Process -Id $c -Force -ErrorAction SilentlyContinue }}"], 20000);
        try { File.Delete(Config.PidFile); } catch { /* best effort */ }
    }
}
