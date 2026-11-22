namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

/// <summary>GPU alert thresholds (docs/agent-upgrade.md §2.3, §7.6).</summary>
public static class GpuAlertRules
{
    public const string HostWide = "*";
    public const double DefaultWarnC = 85, DefaultCritC = 92, MarginC = 5, VramFull = 0.95, IdleUtil = 1;
    public static readonly TimeSpan Debounce = TimeSpan.FromMinutes(2), IdleHold = TimeSpan.FromMinutes(30);
    private static readonly HashSet<string> Slowdown = ["hw_slowdown", "hw_thermal", "hw_power_brake", "sw_thermal"];

    public static IEnumerable<GpuAlertCondition> Evaluate(string gpuState, IReadOnlyList<GpuSampleIn> gpus, IReadOnlySet<string> allocated)
    {
        if (gpuState == "driver_error") return [new GpuAlertCondition(HostWide, "lost", "crit", Debounce, "GPU driver error")];
        return gpus.SelectMany(g => ForGpu(g, allocated.Contains(g.Uuid)));
    }

    private static IEnumerable<GpuAlertCondition> ForGpu(GpuSampleIn g, bool allocated)
    {
        var name = $"GPU {g.Index}";
        var crit = g.TempLimit ?? DefaultCritC;
        var warn = g.TempLimit is { } limit ? limit - MarginC : DefaultWarnC;
        if (g.Temp >= crit) yield return new(g.Uuid, "hot", "crit", Debounce, $"{name}: {g.Temp:0} °C");
        else if (g.Temp >= warn) yield return new(g.Uuid, "hot", "warn", Debounce, $"{name}: {g.Temp:0} °C");
        if (g.VramTotal > 0 && (double)g.VramUsed / g.VramTotal >= VramFull) yield return new(g.Uuid, "vram_full", "warn", Debounce, $"{name}: VRAM {100.0 * g.VramUsed / g.VramTotal:0}%");
        if (g.Throttle.Any(Slowdown.Contains)) yield return new(g.Uuid, "throttle", "warn", Debounce, $"{name}: {string.Join(", ", g.Throttle)}");
        if (g.Ecc is > 0) yield return new(g.Uuid, "ecc", "crit", TimeSpan.Zero, $"{name}: ECC {g.Ecc}");
        if (allocated && g.Util < IdleUtil) yield return new(g.Uuid, "idle_allocated", "info", IdleHold, $"{name}: idle");
    }

    public static string NotificationKind(string kind) => "gpu." + kind;
}
