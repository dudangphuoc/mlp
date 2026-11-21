using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tuning;

[RequiresPermission("run.read")]
public sealed record ListSweepsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<SweepRow>>;

public sealed class ListSweepsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListSweepsQuery, IReadOnlyList<SweepRow>>
{
    public async ValueTask<IReadOnlyList<SweepRow>> Handle(ListSweepsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListSweepsAsync(q.WorkspaceId, ct);
    }
}
