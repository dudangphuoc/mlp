using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>Import history of the workspace.</summary>
public sealed record ListHubImportsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<HubImportRow>>;

/// <summary>Handles <see cref="ListHubImportsQuery"/>.</summary>
public sealed class ListHubImportsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListHubImportsQuery, IReadOnlyList<HubImportRow>>
{
    public async ValueTask<IReadOnlyList<HubImportRow>> Handle(ListHubImportsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListImportsAsync(q.WorkspaceId, ct);
    }
}
