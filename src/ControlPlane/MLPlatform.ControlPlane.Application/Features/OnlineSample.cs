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

public sealed record OnlineSampleQuery(Guid WorkspaceId, Guid ViewId, int Limit) : IQuery<IReadOnlyList<OnlineRow>>;

public sealed class OnlineSampleQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<OnlineSampleQuery, IReadOnlyList<OnlineRow>>
{
    public async ValueTask<IReadOnlyList<OnlineRow>> Handle(OnlineSampleQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetViewAsync(q.ViewId, q.WorkspaceId, ct) ?? throw new NotFoundException("FEATURE_VIEW_NOT_FOUND", "Feature view not found");
        return await db.OnlineSampleAsync(q.ViewId, q.Limit is > 0 and <= 500 ? q.Limit : 50, ct);
    }
}
