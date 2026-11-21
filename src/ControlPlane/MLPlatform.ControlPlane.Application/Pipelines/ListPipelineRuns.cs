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

public sealed record ListPipelineRunsQuery(Guid WorkspaceId, Guid? PipelineId) : IQuery<IReadOnlyList<PipelineRunRow>>;

public sealed class ListPipelineRunsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListPipelineRunsQuery, IReadOnlyList<PipelineRunRow>>
{
    public async ValueTask<IReadOnlyList<PipelineRunRow>> Handle(ListPipelineRunsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListPipelineRunsAsync(q.WorkspaceId, q.PipelineId, ct);
    }
}
