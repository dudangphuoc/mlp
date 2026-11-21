using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>For M09: the version's file list with presigned GETs (24 hours) so the shim can download into `{data}`
/// (20/08 §5.1 step 2).</summary>
public sealed record ResolveVersionFilesQuery(Guid WorkspaceId, Guid VersionId) : IQuery<(VersionRow Version, string Task, IReadOnlyList<VersionFileLink> Files)>;

public sealed class ResolveVersionFilesQueryHandler(IReadSession db, S3Client s3, WorkspaceAuthorizer auth)
    : IQueryHandler<ResolveVersionFilesQuery, (VersionRow Version, string Task, IReadOnlyList<VersionFileLink> Files)>
{
    public async ValueTask<(VersionRow Version, string Task, IReadOnlyList<VersionFileLink> Files)> Handle(ResolveVersionFilesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        var v = await db.GetVersionAsync(q.VersionId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var ds = await db.GetDatasetAsync(v.DatasetId, q.WorkspaceId, ct);
        return (v, ds!.Task, await VersionFileUrls.SignAsync(db, s3, v, ct));
    }
}
