using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed record JobRow(Guid Id, Guid WorkspaceId, string Kind, string Name, string State, string? StateReason, int AttemptCount,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? LastExitReason);
