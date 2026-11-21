using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Paths of the latest version that leave with the next one.</summary>
public sealed record ListPendingRemovalsQuery(Guid WorkspaceId, Guid DatasetId) : IQuery<IReadOnlyList<string>>;

/// <summary>Reads the removal marks.</summary>
public sealed class ListPendingRemovalsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListPendingRemovalsQuery, IReadOnlyList<string>>
{
    public async ValueTask<IReadOnlyList<string>> Handle(ListPendingRemovalsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        return (await db.ListPendingRemovalsAsync(q.DatasetId, ct)).Select(r => r.Path).ToList();
    }
}
