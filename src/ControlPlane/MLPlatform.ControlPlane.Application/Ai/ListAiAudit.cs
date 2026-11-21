using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

[Access(AccessKind.InstanceAdmin)]
public sealed record ListAiAuditQuery(int Limit) : IQuery<List<AiAuditRow>>;

public sealed class ListAiAuditQueryHandler(IReadSession db, InstanceAdminGuard guard) : IQueryHandler<ListAiAuditQuery, List<AiAuditRow>>
{
    public async ValueTask<List<AiAuditRow>> Handle(ListAiAuditQuery q, CancellationToken ct)
    {
        await guard.RequireAsync(ct);
        return await db.ListAiAuditAsync(Math.Clamp(q.Limit <= 0 ? 100 : q.Limit, 1, 500), ct);
    }
}
