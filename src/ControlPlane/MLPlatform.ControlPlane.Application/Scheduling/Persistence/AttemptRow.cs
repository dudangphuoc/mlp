using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Scheduling;

public sealed record AttemptRow(Guid Id, Guid JobId, int Number, Guid[] HostIds, string State, string Spec, Guid WorkspaceId);
