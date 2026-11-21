using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>Import job by id (worker).</summary>
public sealed record GetHubImportJobQuery(Guid Id) : IQuery<HubImportJob?>;

/// <summary>Handles <see cref="GetHubImportJobQuery"/>.</summary>
public sealed class GetHubImportJobQueryHandler(IReadSession db) : IQueryHandler<GetHubImportJobQuery, HubImportJob?>
{
    public async ValueTask<HubImportJob?> Handle(GetHubImportJobQuery q, CancellationToken ct) => await db.GetImportAsync(q.Id, ct);
}
