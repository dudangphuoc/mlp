using System.Runtime.InteropServices;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Ops;

/// <summary>Linux /proc details: parent pid, owner, command line and the Docker container of a process.</summary>
public static partial class LinuxProc
{
    private const int SigTerm = 15;

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int SysKill(int pid, int signal);

    public static void Terminate(int pid) => SysKill(pid, SigTerm);

    public static void Fill(ProcessInfo row, Dictionary<string, string> containers, Dictionary<string, string> users)
    {
        var dir = $"/proc/{row.Pid}";
        try
        {
            var cmd = File.ReadAllText(dir + "/cmdline").Replace('\0', ' ').Trim();
            row.Command = cmd.Length > 400 ? cmd[..400] : cmd;
            foreach (var line in File.ReadLines(dir + "/status"))
            {
                if (line.StartsWith("PPid:", StringComparison.Ordinal) && int.TryParse(line[5..].Trim(), out var ppid)) row.Ppid = ppid;
                else if (line.StartsWith("Uid:", StringComparison.Ordinal))
                    row.User = users.GetValueOrDefault(line[4..].Trim().Split('\t', ' ')[0], line[4..].Trim().Split('\t', ' ')[0]);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (Gpu.Cgroup.ContainerOf(row.Pid) is { } id) row.Container = containers.GetValueOrDefault(id, id[..12]);
    }

    public static Dictionary<string, string> Users()
    {
        try
        {
            return File.ReadLines("/etc/passwd").Select(l => l.Split(':')).Where(f => f.Length > 2)
                .GroupBy(f => f[2]).ToDictionary(g => g.Key, g => g.First()[0]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}
