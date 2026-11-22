using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Ops;

/// <summary>Process list with CPU % from two samples 500 ms apart; Linux adds ppid, user, command line and
/// container.</summary>
public static class ProcessTable
{
    public const int Limit = 300;
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(500);

    public static async Task<CommandResult> ListAsync(ProcessList req, CancellationToken ct)
    {
        var before = CpuTimes();
        await Task.Delay(Window, ct);
        var after = CpuTimes();
        var containers = OperatingSystem.IsLinux() ? await ContainerNamesAsync(ct) : [];
        var users = OperatingSystem.IsLinux() ? LinuxProc.Users() : [];
        var rows = new List<ProcessInfo>(after.Count);
        foreach (var p in Process.GetProcesses())
            using (p)
                if (Row(p, before, after, containers, users) is { } row) rows.Add(row);
        var sorted = req.Sort == "memory" ? rows.OrderByDescending(r => r.RssBytes) : rows.OrderByDescending(r => r.CpuPercent).ThenByDescending(r => r.RssBytes);
        var listing = new ProcessListing { Total = rows.Count };
        listing.Processes.AddRange(sorted.Take(Limit));
        return new CommandResult { Ok = true, ProcessListing = listing };
    }

    private static ProcessInfo? Row(Process p, Dictionary<int, TimeSpan> before, Dictionary<int, TimeSpan> after,
        Dictionary<string, string> containers, Dictionary<string, string> users)
    {
        try
        {
            var row = new ProcessInfo { Pid = p.Id, Name = p.ProcessName, RssBytes = (ulong)Math.Max(0, p.WorkingSet64) };
            if (before.TryGetValue(p.Id, out var b) && after.TryGetValue(p.Id, out var a))
                row.CpuPercent = Math.Round((a - b).TotalMilliseconds / Window.TotalMilliseconds * 100, 1);
            try { row.StartedAt = Timestamp.FromDateTime(p.StartTime.ToUniversalTime()); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            if (OperatingSystem.IsLinux()) LinuxProc.Fill(row, containers, users);
            return row;
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    private static Dictionary<int, TimeSpan> CpuTimes()
    {
        var res = new Dictionary<int, TimeSpan>();
        foreach (var p in Process.GetProcesses())
            using (p)
                try { res[p.Id] = p.TotalProcessorTime; }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        return res;
    }

    private static async Task<Dictionary<string, string>> ContainerNamesAsync(CancellationToken ct)
    {
        var (code, outp, _) = await Docker.RunAsync(["ps", "--no-trunc", "--format", "{{.ID}}|{{.Names}}"], ct, TimeSpan.FromSeconds(10));
        if (code != 0) return [];
        return outp.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('|')).Where(f => f.Length == 2)
            .ToDictionary(f => f[0], f => f[1]);
    }

    public static CommandResult Kill(ProcessKill k)
    {
        if (k.Pid <= 1 || k.Pid == System.Environment.ProcessId) return OpsResult.Fail("FLEET_PROCESS_PROTECTED", "This process cannot be stopped");
        Process p;
        try { p = Process.GetProcessById(k.Pid); }
        catch (ArgumentException) { return OpsResult.Fail("FLEET_PROCESS_NOT_FOUND", "Process does not exist"); }
        using (p)
        {
            if (p.ProcessName is "dockerd" or "containerd" or "com.docker.backend" or "systemd" or "init")
                return OpsResult.Fail("FLEET_PROCESS_PROTECTED", "This process cannot be stopped");
            if (k.Force || k.Tree || OperatingSystem.IsWindows()) p.Kill(k.Tree);
            else LinuxProc.Terminate(k.Pid);
        }
        return new CommandResult { Ok = true };
    }
}
