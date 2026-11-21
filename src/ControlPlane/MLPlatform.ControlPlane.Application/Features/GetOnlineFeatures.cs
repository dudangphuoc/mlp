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

/// <summary>Online: latest value per entity; past TTL counts as missing (stale values are never returned silently).</summary>
public sealed record GetOnlineFeaturesQuery(Guid WorkspaceId, IReadOnlyList<string> Keys, IReadOnlyList<string> Refs) : IQuery<IReadOnlyList<OnlineValue>>;

public sealed class GetOnlineFeaturesQueryHandler(IReadSession db, IClock clock, WorkspaceAuthorizer auth) : IQueryHandler<GetOnlineFeaturesQuery, IReadOnlyList<OnlineValue>>
{
    public async ValueTask<IReadOnlyList<OnlineValue>> Handle(GetOnlineFeaturesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await OnlineFeatures.LookupAsync(db, clock, q.WorkspaceId, q.Keys, q.Refs, null, ct);
    }
}
