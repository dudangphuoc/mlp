using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

[RequiresPermission("dataset.read")]
public sealed record GetDatasetContentStatsQuery(Guid WorkspaceId, Guid DatasetId, Guid? VersionId) : IQuery<DatasetContentStats>;

public sealed class GetDatasetContentStatsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetDatasetContentStatsQuery, DatasetContentStats>
{
    public async ValueTask<DatasetContentStats> Handle(GetDatasetContentStatsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var rows = await db.FileExtensionStatsAsync(q.VersionId, q.DatasetId, ct);
        return new DatasetContentStats(rows, rows.Sum(r => r.Files), rows.Sum(r => r.SizeBytes));
    }
}
