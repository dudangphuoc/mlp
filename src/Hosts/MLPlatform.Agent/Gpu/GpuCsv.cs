using System.Globalization;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent.Gpu;

/// <summary>Parses nvidia-smi CSV (noheader, nounits); "[N/A]", "N/A" and "[Not Supported]" read as absent.</summary>
public static class GpuCsv
{
    public const string Fields = "index,uuid,utilization.gpu,utilization.memory,memory.used,memory.total,temperature.gpu,power.draw," +
        "power.limit,clocks.sm,clocks.max.sm,fan.speed,pstate,{reasons}.active,ecc.errors.uncorrected.volatile.total,temperature.gpu.tlimit";

    public const string BasicFields = "index,uuid,utilization.gpu,utilization.memory,memory.used,memory.total,temperature.gpu,power.draw";

    private const double Mib = 1024d * 1024;

    public static double? Num(string s)
    {
        var t = s.Trim().TrimStart('[').TrimEnd(']');
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(t[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h) ? h : null;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static List<GpuSample> Samples(string csv)
    {
        var list = new List<GpuSample>();
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = line.Split(',').Select(x => x.Trim()).ToArray();
            if (f.Length < 8) continue;
            var g = new GpuSample
            {
                Index = (int)(Num(f[0]) ?? 0), Uuid = f[1], UtilPercent = Num(f[2]) ?? 0,
                MemoryUsedBytes = (ulong)((Num(f[4]) ?? 0) * Mib), MemoryTotalBytes = (ulong)((Num(f[5]) ?? 0) * Mib),
                TemperatureC = Num(f[6]) ?? 0, PowerW = Num(f[7]) ?? 0,
            };
            if (Num(f[3]) is { } mu) g.MemoryUtilPercent = mu;
            if (f.Length >= 16) Extended(g, f);
            list.Add(g);
        }
        return list;
    }

    private static void Extended(GpuSample g, string[] f)
    {
        if (Num(f[8]) is { } pl) g.PowerLimitW = pl;
        if (Num(f[9]) is { } sm) g.SmClockMhz = sm;
        if (Num(f[10]) is { } smMax) g.SmClockMaxMhz = smMax;
        if (Num(f[11]) is { } fan) g.FanPercent = fan;
        g.Pstate = f[12].StartsWith('[') ? "" : f[12];
        if (Num(f[13]) is { } mask) g.ThrottleReasons.AddRange(Reasons((long)mask));
        if (Num(f[14]) is { } ecc) g.EccUncorrected = (ulong)ecc;
        if (Num(f[15]) is { } margin) g.TemperatureLimitC = g.TemperatureC + margin;
    }

    /// <summary>Clock event reason bits that mean the GPU is being slowed down (idle/app-clock bits are ignored).</summary>
    public static IEnumerable<string> Reasons(long mask)
    {
        if ((mask & 0x4) != 0) yield return "sw_power_cap";
        if ((mask & 0x8) != 0) yield return "hw_slowdown";
        if ((mask & 0x20) != 0) yield return "sw_thermal";
        if ((mask & 0x40) != 0) yield return "hw_thermal";
        if ((mask & 0x80) != 0) yield return "hw_power_brake";
    }

    /// <summary>--query-compute-apps=gpu_uuid,pid,used_memory rows.</summary>
    public static List<(string Uuid, int Pid, ulong Bytes)> Apps(string csv)
    {
        var list = new List<(string, int, ulong)>();
        foreach (var line in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = line.Split(',').Select(x => x.Trim()).ToArray();
            if (f.Length < 3 || Num(f[1]) is not { } pid) continue;
            list.Add((f[0], (int)pid, (ulong)((Num(f[2]) ?? 0) * Mib)));
        }
        return list;
    }
}
