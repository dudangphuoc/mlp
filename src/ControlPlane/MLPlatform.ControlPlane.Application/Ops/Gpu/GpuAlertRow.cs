namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

public sealed record GpuAlertRow(Guid HostId, string GpuUuid, string Kind, string Level, DateTimeOffset Since, DateTimeOffset? NotifiedAt);
