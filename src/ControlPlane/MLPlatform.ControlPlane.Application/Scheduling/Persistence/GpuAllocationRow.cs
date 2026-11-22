namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>A live allocation on a host: which attempt/job holds which GPUs and how much memory it reserved.</summary>
public sealed record GpuAllocationRow(Guid AttemptId, Guid JobId, string JobName, string[] GpuUuids, string Spec);
