using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

public sealed record GetHostQuery(Guid WorkspaceId, Guid HostId) : IQuery<HostRow>;

public sealed class GetHostQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetHostQuery, HostRow>
{
    public async ValueTask<HostRow> Handle(GetHostQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        return await db.GetHostAsync(q.HostId, q.WorkspaceId, ct)
               ?? throw new NotFoundException("FLEET_HOST_NOT_FOUND", "Machine not found");
    }
}
