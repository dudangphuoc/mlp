using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Run counts and best result per project of a workspace (project list cards).</summary>
[RequiresPermission("run.read")]
public sealed record ListProjectStatsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ProjectStatsRow>>;

/// <summary>Reads the per-project statistics after the workspace permission check.</summary>
public sealed class ListProjectStatsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListProjectStatsQuery, IReadOnlyList<ProjectStatsRow>>
{
    public async ValueTask<IReadOnlyList<ProjectStatsRow>> Handle(ListProjectStatsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ProjectStatsAsync(q.WorkspaceId, ct);
    }
}
