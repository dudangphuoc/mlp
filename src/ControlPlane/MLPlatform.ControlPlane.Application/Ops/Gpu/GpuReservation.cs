namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

/// <summary>Memory reserved on one GPU by a live allocation (0 = the whole GPU).</summary>
public sealed record GpuReservation(string GpuUuid, Guid AttemptId, Guid JobId, string JobName, long ReservedMb);
