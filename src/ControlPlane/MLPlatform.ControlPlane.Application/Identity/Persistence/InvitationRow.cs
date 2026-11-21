using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

public sealed record InvitationRow(Guid Id, Guid WorkspaceId, string Email, string Role, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt);
