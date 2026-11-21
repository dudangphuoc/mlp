using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>What another module shows next to its own record of a job: state, reason, progress.</summary>
public sealed record JobInfoRow(Guid Id, string State, string? StateReason, DateTimeOffset? StartedAt, int AttemptCount, string? Progress, DateTimeOffset? ProgressAt);
