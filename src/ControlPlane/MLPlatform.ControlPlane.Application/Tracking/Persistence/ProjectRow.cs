using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

public sealed record ProjectRow(Guid Id, Guid WorkspaceId, string Slug, string Name, string Task, DateTimeOffset CreatedAt);
