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

/// <summary>Runs of a project as the run list shows them.</summary>
[RequiresPermission("run.read")]
public sealed record ListRunViewsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<IReadOnlyList<RunView>>;

/// <summary>Lists the project runs and attaches job, dataset and user details.</summary>
public sealed class ListRunViewsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListRunViewsQuery, IReadOnlyList<RunView>>
{
    public async ValueTask<IReadOnlyList<RunView>> Handle(ListRunViewsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await RunViews.BuildAsync(db, await db.ListRunsAsync(q.ProjectId, q.WorkspaceId, ct), ct);
    }
}
