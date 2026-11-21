using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

public sealed record ServerRow(Guid Id, Guid WorkspaceId, Guid UserId, string Username, string Name, string Profile, string Image, string Ide,
    double Cpus, int MemoryGb, int Gpus, Guid? HostId, string? Endpoint, Guid? JobId, string State, string? StateReason, int IdleTimeoutMin,
    DateTimeOffset LastActivityAt, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? StoppedAt);
