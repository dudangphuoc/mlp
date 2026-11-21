using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Platform;

/// <summary>Routing queries.</summary>
public static partial class RouteSql
{
    [SqlQuery("Platform/ListWorkspaceSlugs.sql")]
    public static partial Task<List<WorkspaceSlugRow>> ListWorkspaceSlugsAsync(this IReadSession db, CancellationToken ct);
}
