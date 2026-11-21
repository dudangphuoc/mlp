using System.Security.Cryptography;
using System.Text;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Audit;

[RequiresPermission("audit.read")]
public sealed record ListAuditQuery(Guid WorkspaceId) : IQuery<(IReadOnlyList<AuditRow> Events, bool ChainValid)>;

public sealed class ListAuditQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListAuditQuery, (IReadOnlyList<AuditRow> Events, bool ChainValid)>
{
    public async ValueTask<(IReadOnlyList<AuditRow> Events, bool ChainValid)> Handle(ListAuditQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "audit.read", ct);
        var all = await db.AllEventsAsync(ct);
        var prev = "genesis";
        var valid = true;
        foreach (var e in all)
        {
            if (e.PrevHash != prev || e.Hash != AuditChain.Hash(e.PrevHash, e.Id, e.At, e.WorkspaceId, e.ActorId, e.Action, e.Outcome, e.Reason)) { valid = false; break; }
            prev = e.Hash;
        }
        return (await db.ListAuditEventsAsync(q.WorkspaceId, ct), valid);
    }
}
