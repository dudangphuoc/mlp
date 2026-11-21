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

public sealed record GetRunQuery(Guid WorkspaceId, Guid RunId) : IQuery<RunRow>;

public sealed class GetRunQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetRunQuery, RunRow>
{
    public async ValueTask<RunRow> Handle(GetRunQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var r = await db.GetRunAsync(q.RunId, ct);
        return r is not null && r.WorkspaceId == q.WorkspaceId ? r : throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
    }
}
