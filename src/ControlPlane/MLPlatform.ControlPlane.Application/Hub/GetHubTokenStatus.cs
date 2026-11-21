using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>HF token status (visible to members, no secret — M01 R10).</summary>
[RequiresPermission("dataset.read")]
public sealed record GetHubTokenStatusQuery(Guid WorkspaceId) : IQuery<IntegrationRow?>;

/// <summary>Handles <see cref="GetHubTokenStatusQuery"/>.</summary>
public sealed class GetHubTokenStatusQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetHubTokenStatusQuery, IntegrationRow?>
{
    public async ValueTask<IntegrationRow?> Handle(GetHubTokenStatusQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.GetIntegrationAsync(q.WorkspaceId, "huggingface", ct);
    }
}
