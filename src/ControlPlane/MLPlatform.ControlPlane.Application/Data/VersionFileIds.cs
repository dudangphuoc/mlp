using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Cache ids and sizes of a version's files.</summary>
[RequiresPermission("dataset.read")]
public sealed record VersionFileIdsQuery(Guid WorkspaceId, Guid VersionId) : IQuery<(Guid DatasetId, IReadOnlyList<(string? Id, long Size)> Files)>;

/// <summary>A version's files as cache ids and sizes (no links) — for transfer estimates.</summary>
public sealed class VersionFileIdsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<VersionFileIdsQuery, (Guid DatasetId, IReadOnlyList<(string? Id, long Size)> Files)>
{
    public async ValueTask<(Guid DatasetId, IReadOnlyList<(string? Id, long Size)> Files)> Handle(VersionFileIdsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        var v = await db.GetVersionAsync(q.VersionId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        return (v.DatasetId, (await db.ListVersionFilesAsync(v.Id, ct)).Select(f => (DataCacheRules.Id(v, f.Path, f.Url, f.SizeBytes), f.SizeBytes)).ToList());
    }
}
