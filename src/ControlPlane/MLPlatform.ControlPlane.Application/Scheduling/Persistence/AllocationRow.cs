using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed record AllocationRow(Guid HostId, string[] GpuUuids, string NodeIp);
