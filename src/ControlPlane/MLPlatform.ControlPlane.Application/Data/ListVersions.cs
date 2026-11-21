using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

[RequiresPermission("dataset.read")]
public sealed record ListVersionsQuery(Guid WorkspaceId, Guid DatasetId) : IQuery<IReadOnlyList<VersionRow>>;

public sealed class ListVersionsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListVersionsQuery, IReadOnlyList<VersionRow>>
{
    public async ValueTask<IReadOnlyList<VersionRow>> Handle(ListVersionsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        return await db.ListVersionsAsync(q.DatasetId, ct);
    }
}
