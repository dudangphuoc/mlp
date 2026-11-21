using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

[RequiresPermission("dataset.read")]
public sealed record ListDatasetsQuery(Guid WorkspaceId, string Task) : IQuery<IReadOnlyList<DatasetRow>>;

public sealed class ListDatasetsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListDatasetsQuery, IReadOnlyList<DatasetRow>>
{
    public async ValueTask<IReadOnlyList<DatasetRow>> Handle(ListDatasetsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListDatasetsAsync(q.WorkspaceId, q.Task, ct);
    }
}
