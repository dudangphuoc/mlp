using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet;

public sealed record EnrollmentRow(Guid Id, Guid WorkspaceId, string DataPath, string ModelsPath,
    DateTimeOffset ExpiresAt, DateTimeOffset? UsedAt);
