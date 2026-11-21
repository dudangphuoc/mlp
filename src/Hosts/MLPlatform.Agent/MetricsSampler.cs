using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using MLPlatform.AgentProtocol;
using MLPlatform.Contracts.V1;
using Environment = System.Environment;   // Contracts.V1 also has an Environment message (catalog)

namespace MLPlatform.Agent;

/// <summary>Host + container + GPU metrics every 15 s (20/10 §2.5): `docker stats` for platform containers, `nvidia-smi` when a GPU is present.</summary>
public static class MetricsSampler
{
    public static async Task<HostMetrics> SampleAsync(CancellationToken ct)
    {
        var m = new HostMetrics { At = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(TimeProvider.System.GetUtcNow()) };
        var (code, outp, _) = await Docker.RunAsync(["ps", "-q", "--filter", "label=mlp.managed=1"], ct);
        var ids = code == 0 ? outp.Split('\n', StringSplitOptions.RemoveEmptyEntries) : [];
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
            if (sc == 0)
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
        try
        {
            var (gc, gout, _) = await RunAsync("nvidia-smi", ["--query-gpu=uuid,utilization.gpu,memory.used,temperature.gpu,power.draw", "--format=csv,noheader,nounits"], ct);
            if (gc == 0)
                foreach (var line in gout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = line.Split(',').Select(x => x.Trim()).ToArray();
                    if (p.Length < 5) continue;
                    double D(string x) => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
                    m.Gpus.Add(new GpuSample { Uuid = p[0], UtilPercent = D(p[1]), MemoryUsedBytes = (ulong)(D(p[2]) * 1024 * 1024), TemperatureC = D(p[3]), PowerW = D(p[4]) });
                }
        }
        catch (System.ComponentModel.Win32Exception) { }   // no nvidia-smi
        return m;
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

    private static async Task<(int, string, string)> RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = await p.StandardOutput.ReadToEndAsync(ct);
        var e = await p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, o, e);
    }
}
