using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MLPlatform.AgentProtocol;
using MLPlatform.Contracts.V1;
using Environment = System.Environment;

namespace MLPlatform.Agent;

/// <summary>Host + container + GPU metrics every 15 s (20/10 §2.5): `docker stats` for platform containers,
/// `nvidia-smi` when a GPU is present.</summary>
public static class MetricsSampler
{
    public static async Task<HostMetrics> SampleAsync(AgentOptions o, CancellationToken ct)
    {
        var m = new HostMetrics { At = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(TimeProvider.System.GetUtcNow()) };
        var (code, outp, _) = await Docker.RunAsync(["ps", "--filter", "label=mlp.managed=1", "--format", "{{.ID}}|{{.Label \"mlp.agent\"}}"], ct);
        var ids = code == 0 ? OwnedIds(outp) : [];
        if (code != 0)
        {
            var (_, _, err) = await Docker.RunAsync(["info", "--format", "{{.ServerVersion}}"], ct, TimeSpan.FromSeconds(10));
            m.DockerDown = true;
            var line = err.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "docker is not responding";
            m.DockerError = line.Length > 240 ? line[..240] : line;
        }
        if (ids.Length > 0)
        {
            var (sc, stats, _) = await Docker.RunAsync(["stats", "--no-stream", "--format", "{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}", .. ids], ct, TimeSpan.FromSeconds(20));
            if (sc == 0) AddContainers(m, stats);
        }
        var gpu = await Gpu.GpuSampler.SampleAsync(o, ct);
        m.GpuState = gpu.State;
        m.GpuError = gpu.Error;
        m.Gpus.AddRange(gpu.Gpus);
        m.GpuProcesses.AddRange(gpu.Processes);
        m.GpuYield = gpu.Yield;
        return m;
    }

    /// <summary>Ids of the listed containers this agent owns ("id|owner" lines).</summary>
    public static string[] OwnedIds(string psOutput) =>
        [.. psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split('|'))
            .Where(p => HostOwnership.Owns(p.Length > 1 && p[1].Length > 0 ? new Dictionary<string, string> { [HostOwnership.Label] = p[1] } : []))
            .Select(p => p[0])];

    private static void AddContainers(HostMetrics m, string stats)
    {
        foreach (var line in stats.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('|');
            if (p.Length < 3) continue;
            m.Containers.Add(new ContainerSample
            {
                Name = p[0], State = "running",
                CpuPercent = double.TryParse(p[1].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var cpu) ? cpu : 0,
                MemoryBytes = Bytes(p[2].Split('/')[0].Trim()),
            });
        }
    }

    /// <summary>"12.3MiB" / "1.2GiB" / "512kB" → byte.</summary>
    public static ulong Bytes(string s)
    {
        var i = 0;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
        if (!double.TryParse(s[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0;
        var mul = s[i..].Trim().ToUpperInvariant() switch
        {
            "KIB" or "KB" => 1024d, "MIB" or "MB" => 1024d * 1024, "GIB" or "GB" => 1024d * 1024 * 1024, "TIB" or "TB" => 1024d * 1024 * 1024 * 1024, _ => 1d,
        };
        return (ulong)(v * mul);
    }
}
