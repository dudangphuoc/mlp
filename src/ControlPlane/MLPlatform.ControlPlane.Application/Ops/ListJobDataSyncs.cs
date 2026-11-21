using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Dataset syncs of a job (newest first) for the job page.</summary>
public sealed record ListJobDataSyncsQuery(Guid WorkspaceId, Guid JobId) : IQuery<IReadOnlyList<DataSyncRow>>;

/// <summary>Reads the stored dataset syncs of a job.</summary>
public sealed class ListJobDataSyncsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListJobDataSyncsQuery, IReadOnlyList<DataSyncRow>>
{
    public async ValueTask<IReadOnlyList<DataSyncRow>> Handle(ListJobDataSyncsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListJobDataSyncsAsync(q.WorkspaceId, q.JobId, ct);
    }
}
