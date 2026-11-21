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

[RequiresPermission("run.read")]
public sealed record ListProjectsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ProjectRow>>;

public sealed class ListProjectsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListProjectsQuery, IReadOnlyList<ProjectRow>>
{
    public async ValueTask<IReadOnlyList<ProjectRow>> Handle(ListProjectsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListProjectsAsync(q.WorkspaceId, ct);
    }
}
