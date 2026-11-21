using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

public sealed record RunInfoRow(Guid Id, Guid WorkspaceId, Guid UserId, Guid ProjectId, string Name);
