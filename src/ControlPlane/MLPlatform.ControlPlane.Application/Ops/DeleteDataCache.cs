using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>
/// Deletes a machine's cache of one dataset (also how a full re-download is forced). Refused while a job using it is active; the caller sends
/// the volume removal to the agent after this succeeds.
/// </summary>
public sealed record DeleteDataCacheCommand(Guid WorkspaceId, Guid HostId, Guid DatasetId) : ICommand<string>;

/// <summary>Checks the cache is idle, marks it removed and returns the volume name to remove on the host.</summary>
public sealed class DeleteDataCacheCommandHandler(IWriteSession db, IClock clock, IMediator mediator, WorkspaceAuthorizer auth) : ICommandHandler<DeleteDataCacheCommand, string>
{
    public async ValueTask<string> Handle(DeleteDataCacheCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "fleet.manage", ct);
        var row = (await DataCacheRow.WithUseAsync(mediator, await db.ListHostDataCachesAsync(c.WorkspaceId, c.HostId, ct), ct)).FirstOrDefault(r => r.DatasetId == c.DatasetId)
            ?? throw new NotFoundException("FLEET_DATA_CACHE_NOT_FOUND", "This machine holds no cache of that dataset");
        if (row.InUse) throw new PreconditionException("FLEET_DATA_CACHE_IN_USE", "A job using this dataset is still active on this machine");
        await db.MarkDataCacheRemovedAsync(c.WorkspaceId, c.HostId, c.DatasetId, clock.UtcNow, ct);
        return MLPlatform.ControlPlane.Application.Data.DataCacheRules.Volume(c.DatasetId);
    }
}
