using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Pipelines;

[RequiresPermission("run.read")]
public sealed record ListPipelinesQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<PipelineRow>>;

public sealed class ListPipelinesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListPipelinesQuery, IReadOnlyList<PipelineRow>>
{
    public async ValueTask<IReadOnlyList<PipelineRow>> Handle(ListPipelinesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListPipelinesAsync(q.WorkspaceId, ct);
    }
}
