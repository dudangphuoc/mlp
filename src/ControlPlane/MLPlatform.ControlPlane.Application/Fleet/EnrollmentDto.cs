using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

public sealed record EnrollmentDto(Guid Id, string Token, DateTimeOffset ExpiresAt, Guid WorkspaceId);
