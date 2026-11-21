using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

/// <summary>Internal: the environment of one image tag on one machine, or null when it was never built or checked from here.</summary>
public sealed record GetEnvironmentRowQuery(Guid WorkspaceId, string EnvTag, Guid HostId) : IQuery<EnvironmentRow?>;

/// <summary>Same staleness rule as the list.</summary>
public sealed class GetEnvironmentRowQueryHandler(IReadSession db, IClock clock) : IQueryHandler<GetEnvironmentRowQuery, EnvironmentRow?>
{
    public async ValueTask<EnvironmentRow?> Handle(GetEnvironmentRowQuery q, CancellationToken ct) =>
        await db.GetEnvironmentAsync(q.WorkspaceId, q.EnvTag, q.HostId, ct) is { } e ? ListEnvironmentRowsQueryHandler.Settle(e, clock.UtcNow) : null;
}
