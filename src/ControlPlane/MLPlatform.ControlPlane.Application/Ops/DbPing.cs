using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

[Access(AccessKind.Internal)]
public sealed record DbPingQuery : IQuery<bool>;

public sealed class DbPingQueryHandler(IReadSession db) : IQueryHandler<DbPingQuery, bool>
{
    public async ValueTask<bool> Handle(DbPingQuery q, CancellationToken ct) => await db.JobWorkspaceAsync(Guid.Empty, ct) is null || true;
}
