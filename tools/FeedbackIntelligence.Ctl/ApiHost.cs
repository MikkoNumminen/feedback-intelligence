using System.Diagnostics;

namespace FeedbackIntelligence.Ctl;

/// <summary>
/// Lifecycle of the .NET API process. Unlike ragctl's dockerized backend, our
/// API is a plain `dotnet` process, so feedctl tracks it by PID file and starts
/// it detached via ShellExecuteEx (Process.Start with UseShellExecute=true),
/// which inherits NO handles — so the long-lived API never holds a copy of the
/// stdout pipe that launched feedctl (that inheritance made the launching shell
/// hang open). The process outlives the feedctl invocation.
/// </summary>
public static class ApiHost
{
    public static int? RunningPid()
    {
        try
        {
            if (!File.Exists(Config.PidFile))
                return null;
            if (!int.TryParse(File.ReadAllText(Config.PidFile).Trim(), out var pid))
                return null;
            using var p = Process.GetProcessById(pid); // throws if not running
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
        // Launch the API FULLY DETACHED and independent. Process.Start with
        // UseShellExecute=true routes through ShellExecuteEx, which inherits NO
        // handles — so the long-lived API never holds a copy of the stdout pipe
        // that launched feedctl. (The old Start-Process -RedirectStandard* forced
        // CreateProcess(bInheritHandles=TRUE) and leaked that pipe, so the
        // launching shell never saw EOF and hung open forever, and the pid read
        // could block.) ArgumentList quotes each value itself (no manual shell
        // escaping), and proc.Id is the REAL dotnet / :5088-owner pid. Working dir
        // = the API PROJECT dir so ASP.NET's ContentRoot finds appsettings.json +
        // wwwroot; DB + snapshot are absolute and CWD-independent. Trade-off:
        // ShellExecuteEx cannot redirect stdout, so a failed start is diagnosed by
        // running the API directly rather than from a captured log.
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = apiDir,
                UseShellExecute = true,              // ShellExecuteEx: inherits NO handles => clean detach
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            psi.ArgumentList.Add(dll);
            psi.ArgumentList.Add("--urls");
            psi.ArgumentList.Add(Config.BaseUrl);
            psi.ArgumentList.Add("--Ingest:DbPath=" + db);
            psi.ArgumentList.Add("--Ingest:LiveDbPath=" + Config.Abs(Config.LiveDbPath));
            psi.ArgumentList.Add("--Report:SnapshotDir=" + snapDir);

            // Dispose the wrapper (frees the handle); UseShellExecute=true detaches the
            // real process, so disposing does not terminate the launched API.
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                Console.WriteLine("  " + Term.C("○ could not start the API process", "31"));
                return null;
            }
            // A non-null proc is not proof it stayed up: a broken dotnet host or DLL
            // exits at once. Treat an instant exit as a fast, honest failure instead
            // of making the caller wait out the 120 s /health poll. On success dotnet
            // stays alive, so WaitForExit(500) is false. (If exit info is unavailable
            // for a shell-executed process, fall through — the /health wait backstops.)
            bool exitedFast;
            try { exitedFast = proc.WaitForExit(500); }
            catch { exitedFast = false; }
            if (exitedFast)
            {
                Console.WriteLine("  " + Term.C("○ the API process exited immediately — check the DLL / dotnet host", "31"));
                return null;
            }
            File.WriteAllText(Config.PidFile, proc.Id.ToString());
            return proc.Id;
        }
        catch (Exception ex)
        {
            // e.g. dotnet not on PATH -> ShellExecuteEx throws Win32Exception; fast, honest fail.
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
