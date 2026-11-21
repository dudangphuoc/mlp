using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Splits of several versions of one dataset, for the version list.</summary>
public sealed record ListVersionSplitsQuery(Guid WorkspaceId, Guid[] VersionIds) : IQuery<IReadOnlyList<VersionSplitRow>>;

/// <summary>One lookup for the whole list.</summary>
public sealed class ListVersionSplitsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListVersionSplitsQuery, IReadOnlyList<VersionSplitRow>>
{
    public async ValueTask<IReadOnlyList<VersionSplitRow>> Handle(ListVersionSplitsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return q.VersionIds.Length == 0 ? [] : await db.VersionSplitsAsync(q.VersionIds, ct);
    }
}
