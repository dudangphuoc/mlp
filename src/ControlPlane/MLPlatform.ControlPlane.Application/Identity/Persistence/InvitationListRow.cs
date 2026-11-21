using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>Invitation in a workspace.</summary>
public sealed record InvitationListRow(Guid Id, string Email, string Role, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt);
