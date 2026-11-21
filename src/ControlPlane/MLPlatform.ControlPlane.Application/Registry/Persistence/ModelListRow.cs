using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Registry;

public sealed record ModelListRow(Guid Id, Guid WorkspaceId, string Name, string Task, DateTimeOffset CreatedAt, int LatestVersion, int ProductionVersion);
