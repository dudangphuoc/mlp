using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Frozen revisions of a project recipe, newest first.</summary>
[RequiresPermission("run.read")]
public sealed record ListRevisionsQuery(Guid WorkspaceId, Guid ProjectRecipeId) : IQuery<(IReadOnlyList<RevisionListRow> Rows, IReadOnlyDictionary<Guid, string> Names)>;

/// <summary>Lists revisions and resolves who froze them in one lookup.</summary>
public sealed class ListRevisionsQueryHandler(IReadSession db, WorkspaceAuthorizer auth)
    : IQueryHandler<ListRevisionsQuery, (IReadOnlyList<RevisionListRow> Rows, IReadOnlyDictionary<Guid, string> Names)>
{
    public async ValueTask<(IReadOnlyList<RevisionListRow> Rows, IReadOnlyDictionary<Guid, string> Names)> Handle(ListRevisionsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var rows = await db.ListRevisionsAsync(q.ProjectRecipeId, q.WorkspaceId, ct);
        var ids = rows.Select(r => r.CreatedBy).Distinct().ToArray();
        var names = ids.Length == 0 ? [] : (await db.UserNamesAsync(ids, ct)).ToDictionary(u => u.Id, u => u.DisplayName);
        return (rows, names);
    }
}
