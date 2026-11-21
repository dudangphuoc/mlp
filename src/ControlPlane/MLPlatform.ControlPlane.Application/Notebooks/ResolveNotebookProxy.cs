using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

/// <summary>Proxy `/nb/{id}/…`: notebook owner only; returns null → 404.</summary>
public sealed record ResolveNotebookProxyQuery(Guid ServerId, Guid UserId) : IQuery<ServerRow?>;

public sealed class ResolveNotebookProxyQueryHandler(IReadSession db) : IQueryHandler<ResolveNotebookProxyQuery, ServerRow?>
{
    public async ValueTask<ServerRow?> Handle(ResolveNotebookProxyQuery q, CancellationToken ct)
    {
        var s = await db.GetServerByIdAsync(q.ServerId, ct);
        return s is not null && s.UserId == q.UserId && s.State == "running" && s.HostId is not null && s.Endpoint is not null ? s : null;
    }
}
