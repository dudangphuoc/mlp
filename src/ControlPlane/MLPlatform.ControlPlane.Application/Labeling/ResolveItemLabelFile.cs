using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Where the label file paired with an item lives (null = none, or labels were already made or imported for
/// it).</summary>
public sealed record ResolveItemLabelFileQuery(Guid WorkspaceId, Guid ProjectId, string Path) : IQuery<string?>;

/// <summary>Only for items without platform labels that were never imported, in a project that labels a
/// version.</summary>
public sealed class ResolveItemLabelFileQueryHandler(IReadSession db, LabelAccess access, S3Client s3) : IQueryHandler<ResolveItemLabelFileQuery, string?>
{
    public async ValueTask<string?> Handle(ResolveItemLabelFileQuery q, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        if (p.VersionId is not { } vid || FileKinds.Of(q.Path) != "image") return null;
        if (await db.LabelsImportedAsync(p.Id, q.Path, ct) == true) return null;
        if ((await db.ListAnnotationsAsync(p.DatasetId, q.Path, ct)).Count > 0) return null;
        var v = await db.GetVersionAsync(vid, q.WorkspaceId, ct);
        if (v is null) return null;
        var found = await db.FindFilesByPathAsync(vid, p.DatasetId, [.. YoloLabelFiles.CandidatesFor(q.Path)], ct);
        var order = YoloLabelFiles.CandidatesFor(q.Path);
        var hit = found.OrderBy(f => order.ToList().IndexOf(f.Path)).FirstOrDefault();
        return hit is null ? null : DatasetFileUrls.For(s3, v, hit.Path, hit.Url, Audience.Internal);
    }
}
