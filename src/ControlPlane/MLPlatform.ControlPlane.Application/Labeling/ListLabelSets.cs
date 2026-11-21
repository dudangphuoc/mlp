using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

[RequiresPermission("dataset.read")]
public sealed record ListLabelSetsQuery(Guid WorkspaceId, Guid DatasetId) : IQuery<IReadOnlyList<LabelSetRow>>;

public sealed class ListLabelSetsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListLabelSetsQuery, IReadOnlyList<LabelSetRow>>
{
    public async ValueTask<IReadOnlyList<LabelSetRow>> Handle(ListLabelSetsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        return await db.ListLabelSetsAsync(q.DatasetId, ct);
    }
}
