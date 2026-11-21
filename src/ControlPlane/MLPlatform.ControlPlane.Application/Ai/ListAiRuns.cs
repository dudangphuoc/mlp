using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

[Access(AccessKind.ScopedPermission, "audit.read")]
public sealed record ListAiRunsQuery(Guid? WorkspaceId, string State, Guid? ModelRef, int Limit) : IQuery<List<AiRunRow>>;

public sealed class ListAiRunsQueryHandler(IReadSession db, WorkspaceAuthorizer auth, ICurrentUser user) : IQueryHandler<ListAiRunsQuery, List<AiRunRow>>
{
    public async ValueTask<List<AiRunRow>> Handle(ListAiRunsQuery q, CancellationToken ct)
    {
        var (ws, instance) = await AiRunAccess.ScopeAsync(db, auth, user, q.WorkspaceId, ct);
        return await db.ListAiRunsAsync(ws, instance, q.State ?? "", q.ModelRef, Math.Clamp(q.Limit <= 0 ? 100 : q.Limit, 1, 500), ct);
    }
}
