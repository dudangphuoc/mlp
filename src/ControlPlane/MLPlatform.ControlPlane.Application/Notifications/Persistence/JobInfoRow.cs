using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

public sealed record JobInfoRow(Guid Id, Guid WorkspaceId, Guid OwnerId, string Kind, string Name, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);
