using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

public sealed record ClusterMapQuery(Guid WorkspaceId) : IQuery<(IReadOnlyList<HostMapRow> Hosts, IReadOnlyList<HostAttemptJobRow> Attempts)>;

public sealed class ClusterMapQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ClusterMapQuery, (IReadOnlyList<HostMapRow> Hosts, IReadOnlyList<HostAttemptJobRow> Attempts)>
{
    public async ValueTask<(IReadOnlyList<HostMapRow> Hosts, IReadOnlyList<HostAttemptJobRow> Attempts)> Handle(ClusterMapQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        return (await db.HostMapAsync(q.WorkspaceId, ct), await db.HostAttemptJobsAsync(q.WorkspaceId, ct));
    }
}
