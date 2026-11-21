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

/// <summary>Checks one workspace permission for the catalog RPCs whose work happens outside the handlers (machines,
/// files on disk).</summary>
public sealed record RequireCatalogPermissionQuery(Guid WorkspaceId, string Permission) : IQuery<bool>;

/// <summary>Throws when the caller is not a member or lacks the permission.</summary>
public sealed class RequireCatalogPermissionQueryHandler(WorkspaceAuthorizer auth) : IQueryHandler<RequireCatalogPermissionQuery, bool>
{
    public async ValueTask<bool> Handle(RequireCatalogPermissionQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, q.Permission, ct);
        return true;
    }
}
