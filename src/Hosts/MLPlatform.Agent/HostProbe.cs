using System.Diagnostics;
using System.Runtime.InteropServices;
using MLPlatform.Contracts.V1;
using Environment = System.Environment;

namespace MLPlatform.Agent;

/// <summary>Collects HostInfo; the agent is the source of truth about the host (D-12, 20/04 §4).</summary>
public static class HostProbe
{
    public static async Task<HostInfo> CollectAsync(AgentOptions o, CancellationToken ct)
    {
        var info = new HostInfo
        {
            MachineName = Environment.MachineName,
            OsFamily = OperatingSystem.IsLinux() ? OsFamily.Linux : OperatingSystem.IsWindows() ? OsFamily.Windows
                : OperatingSystem.IsMacOS() ? OsFamily.Macos : OsFamily.Unspecified,
            OsDescription = RuntimeInformation.OSDescription,
            Kernel = Environment.OSVersion.VersionString,
            Architecture = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            LogicalCpus = Environment.ProcessorCount,
            MemoryBytes = (ulong)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            DataPath = o.DataPath,
            ModelsPath = o.ModelsPath,
            AgentVersion = AgentService.Version,
            AgentRid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
            AgentExe = Environment.ProcessPath ?? "",
            ServiceManager = o.ServiceManager,
            Runtime = OperatingSystem.IsLinux() ? ContainerRuntime.Native
                : OperatingSystem.IsWindows() ? ContainerRuntime.Wsl2 : ContainerRuntime.DockerDesktop,
        };
        foreach (var path in new[] { o.DataPath, o.ModelsPath }.Where(Directory.Exists).Distinct())
        {
            var d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!);
            info.Disks.Add(new DiskInfo { Mount = path, TotalBytes = (ulong)d.TotalSize, FreeBytes = (ulong)d.AvailableFreeSpace });
        }
        info.DockerVersion = (await RunAsync("docker", ["version", "--format", "{{.Server.Version}}"], ct))?.Trim() ?? "";
        var smi = await RunAsync("nvidia-smi", ["--query-gpu=index,uuid,name,memory.total,compute_cap,driver_version",
            "--format=csv,noheader,nounits"], ct);
        if (smi is not null) AddGpus(info, smi);
        return info;
    }

    private static void AddGpus(HostInfo info, string smi)
    {
        foreach (var line in smi.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split(',').Select(x => x.Trim()).ToArray();
            if (f.Length < 6) continue;
            info.Gpus.Add(new GpuInfo
            {
                Index = int.Parse(f[0]), Uuid = f[1], Name = f[2],
                MemoryBytes = ulong.Parse(f[3]) * 1024 * 1024, ComputeCapability = f[4],
            });
            info.NvidiaDriver = f[5];
        }
    }

    /// <summary>Run a command, return stdout; null if the command is missing / fails / exceeds 10 s.</summary>
    public static async Task<string?> RunAsync(string file, string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var output = await p.StandardOutput.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);
            return p.ExitCode == 0 ? output : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
    }
}
