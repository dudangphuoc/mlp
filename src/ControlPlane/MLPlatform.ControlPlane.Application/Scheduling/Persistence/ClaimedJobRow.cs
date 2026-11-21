using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed record ClaimedJobRow(Guid Id, Guid WorkspaceId, string Kind, string Name, string Spec, int AttemptCount, int MaxAttempts);
