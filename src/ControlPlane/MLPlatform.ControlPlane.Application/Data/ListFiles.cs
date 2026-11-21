using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

public sealed record ListFilesQuery(Guid WorkspaceId, Guid DatasetId) : IQuery<IReadOnlyList<FileRow>>;

public sealed class ListFilesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListFilesQuery, IReadOnlyList<FileRow>>
{
    public async ValueTask<IReadOnlyList<FileRow>> Handle(ListFilesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        return await db.ListFilesAsync(q.DatasetId, ct);
    }
}
