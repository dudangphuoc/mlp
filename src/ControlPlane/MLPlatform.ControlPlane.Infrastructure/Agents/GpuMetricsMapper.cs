using MLPlatform.ControlPlane.Application.Ops.Gpu;
using MLPlatform.Contracts.V1;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Maps the agent's GPU readings to application records.</summary>
public static class GpuMetricsMapper
{
    public static RecordGpuMetricsCommand Command(Guid hostId, HostMetrics hm) => new(hostId, State(hm.GpuState),
        [.. hm.Gpus.Select(Sample)], hm.GpuYield?.Active == true);

    public static string State(GpuState s) => s switch
    {
        GpuState.Ok => "ok",
        GpuState.DriverError => "driver_error",
        GpuState.None => "none",
        _ => "unknown",
    };

    private static GpuSampleIn Sample(GpuSample g) => new(g.Uuid, g.Index, g.UtilPercent, g.HasMemoryUtilPercent ? g.MemoryUtilPercent : null,
        (long)g.MemoryUsedBytes, (long)g.MemoryTotalBytes, g.TemperatureC, g.PowerW, g.HasPowerLimitW ? g.PowerLimitW : null,
        g.HasSmClockMhz ? g.SmClockMhz : null, [.. g.ThrottleReasons], g.HasEccUncorrected ? (long)g.EccUncorrected : null,
        g.HasTemperatureLimitC ? g.TemperatureLimitC : null);

    /// <summary>Real VRAM per attempt from this agent's own GPU processes (other agents' containers are ignored).</summary>
    public static IReadOnlyDictionary<Guid, long> AttemptVram(HostMetrics hm, Guid hostId) =>
        hm.GpuProcesses
            .Where(p => Guid.TryParse(p.AttemptId, out _) && (p.OwnerAgent.Length == 0 || p.OwnerAgent == hostId.ToString()))
            .GroupBy(p => Guid.Parse(p.AttemptId))
            .ToDictionary(g => g.Key, g => (long)g.Sum(p => (long)p.UsedMemoryBytes));
}
