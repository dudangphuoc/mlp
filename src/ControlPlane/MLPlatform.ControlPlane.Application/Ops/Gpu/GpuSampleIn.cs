namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

/// <summary>One GPU reading from the agent; absent readings are null.</summary>
public sealed record GpuSampleIn(string Uuid, int Index, double Util, double? MemUtil, long VramUsed, long VramTotal, double Temp, double Power,
    double? PowerLimit, double? SmClock, string[] Throttle, long? Ecc, double? TempLimit);
