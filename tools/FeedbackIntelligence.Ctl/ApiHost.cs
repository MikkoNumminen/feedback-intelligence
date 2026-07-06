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
        // Launch the API FULLY DETACHED via a batch wrapper started with
        // UseShellExecute=true (ShellExecuteEx). This is the load-bearing detail:
        // Start-Process -RedirectStandard* — and Process.Start with redirected
        // streams — force CreateProcess(bInheritHandles=TRUE), so the long-lived
        // API inherited a copy of whatever stdout pipe launched feedctl. That
        // handle never closed, so the launching shell never saw EOF and looked
        // like it was "still running" forever (and the pid read could block, the
        // old `up`/`data` hang). ShellExecuteEx inherits NO handles, so `up`
        // returns clinically and the API is truly independent. The batch does the
        // file redirection (keeping .feedctl/api.log); the working dir is the API
        // PROJECT dir so ASP.NET's ContentRoot finds appsettings.json + wwwroot
        // (DB + snapshot are absolute, CWD-independent). Paths are wrapped in cmd
        // double-quotes (doubled to escape) and any '%' is doubled so cmd does not
        // treat a path as an environment variable.
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"").Replace("%", "%%") + "\"";
        var batch = Config.Abs(Path.Combine(".feedctl", "launch-api.cmd"));
        File.WriteAllText(batch,
            "@echo off\r\n" +
            "dotnet " + Q(dll) + " --urls " + Config.BaseUrl + " " +
            Q("--Ingest:DbPath=" + db) + " " + Q("--Report:SnapshotDir=" + snapDir) +
            " 1>" + Q(LogFile) + " 2>" + Q(LogFile + ".err") + "\r\n");

        try
        {
            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = batch,
                WorkingDirectory = apiDir,
                UseShellExecute = true,              // ShellExecuteEx: inherits NO handles => clean detach
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (proc is null)
            {
                Console.WriteLine("  " + Term.C("○ could not start the API process", "31"));
                return null;
            }
            // proc is the cmd host running the batch; it stays alive as the API's
            // parent, so its pid tracks the API's lifetime and taskkill /T reaps both.
            File.WriteAllText(Config.PidFile, proc.Id.ToString());
            return proc.Id;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  " + Term.C("○ could not start the API process: " + ex.Message, "31"));
            return null;
        }
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
