using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Fleet.Ops;

[RequiresPermission("fleet.ops_read")]
public sealed record AuthorizeHostFileDownloadQuery(Guid WorkspaceId, Guid HostId) : IQuery<bool>;

public sealed class AuthorizeHostFileDownloadQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<AuthorizeHostFileDownloadQuery, bool>
{
    public async ValueTask<bool> Handle(AuthorizeHostFileDownloadQuery r, CancellationToken ct)
    {
        await HostOpsAccess.RequireAsync(auth, db, r.WorkspaceId, r.HostId, "fleet.ops_read", ct);
        return true;
    }
}
