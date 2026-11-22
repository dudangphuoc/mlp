namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

public sealed record GpuPointRow(DateTimeOffset At, double Util, long VramUsed, long VramTotal, double Temp, double Power, double? SmClock);
