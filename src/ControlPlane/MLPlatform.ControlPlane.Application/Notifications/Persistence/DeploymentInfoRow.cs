using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notifications;

public sealed record DeploymentInfoRow(Guid Id, Guid WorkspaceId, Guid CreatedBy, string Slug);
