using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Platform;

/// <summary>All workspaces (id, slug) — internal, not exposed over RPC.</summary>
public sealed record ListWorkspaceSlugsQuery : IQuery<List<WorkspaceSlugRow>>;

/// <summary>Handles <see cref="ListWorkspaceSlugsQuery"/>.</summary>
public sealed class ListWorkspaceSlugsQueryHandler(IReadSession db) : IQueryHandler<ListWorkspaceSlugsQuery, List<WorkspaceSlugRow>>
{
    public async ValueTask<List<WorkspaceSlugRow>> Handle(ListWorkspaceSlugsQuery q, CancellationToken ct) => await db.ListWorkspaceSlugsAsync(ct);
}
