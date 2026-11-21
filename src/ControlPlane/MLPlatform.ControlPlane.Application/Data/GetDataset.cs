using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

[RequiresPermission("dataset.read")]
public sealed record GetDatasetQuery(Guid WorkspaceId, Guid DatasetId) : IQuery<DatasetRow>;

public sealed class GetDatasetQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetDatasetQuery, DatasetRow>
{
    public async ValueTask<DatasetRow> Handle(GetDatasetQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
    }
}
