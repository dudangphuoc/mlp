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

/// <summary>Attempt → (job, workspace) for the log store (path in S3 storage).</summary>
[Access(AccessKind.Internal)]
public sealed record AttemptOwnerQuery(Guid AttemptId) : IQuery<AttemptOwnerRow?>;

public sealed class AttemptOwnerQueryHandler(IReadSession db) : IQueryHandler<AttemptOwnerQuery, AttemptOwnerRow?>
{
    public async ValueTask<AttemptOwnerRow?> Handle(AttemptOwnerQuery q, CancellationToken ct) =>
        await Scheduling.SchedSql.GetAttemptAsync(db, q.AttemptId, ct) is { } a ? new AttemptOwnerRow(a.JobId, a.WorkspaceId) : null;
}
