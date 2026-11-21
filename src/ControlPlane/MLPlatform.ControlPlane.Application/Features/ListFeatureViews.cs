using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Monitoring;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Features;

[RequiresPermission("dataset.read")]
public sealed record ListFeatureViewsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ViewRow>>;

public sealed class ListFeatureViewsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListFeatureViewsQuery, IReadOnlyList<ViewRow>>
{
    public async ValueTask<IReadOnlyList<ViewRow>> Handle(ListFeatureViewsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListViewsAsync(q.WorkspaceId, ct);
    }
}
