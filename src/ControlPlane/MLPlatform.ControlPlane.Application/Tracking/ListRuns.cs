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
public sealed record ListRunsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<IReadOnlyList<RunRow>>;

public sealed class ListRunsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListRunsQuery, IReadOnlyList<RunRow>>
{
    public async ValueTask<IReadOnlyList<RunRow>> Handle(ListRunsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListRunsAsync(q.ProjectId, q.WorkspaceId, ct);
    }
}
