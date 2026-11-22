namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed record GpuJobMarkRow(Guid JobId, string JobName, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);
