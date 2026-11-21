using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Registry;

public sealed record ModelRow(Guid Id, Guid WorkspaceId, string Name, string Task, DateTimeOffset CreatedAt);
