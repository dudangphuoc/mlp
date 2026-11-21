using System.Globalization;
using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>The project's items by status / file name. A Lead sees every item; other members only their own.</summary>
public sealed record ListLabelItemsQuery(Guid WorkspaceId, Guid ProjectId, string Status, string Search, bool Mine, int Limit, int Offset) : IQuery<ItemPage>;

/// <summary>Pages the item states and resolves annotator / reviewer names in one lookup.</summary>
public sealed class ListLabelItemsQueryHandler(IReadSession db, LabelAccess access, MLPlatform.Storage.S3Client s3) : IQueryHandler<ListLabelItemsQuery, ItemPage>
{
    public async ValueTask<ItemPage> Handle(ListLabelItemsQuery q, CancellationToken ct)
    {
        var (uid, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        var mine = q.Mine || !roles.Contains("lead");
        var take = q.Limit is > 0 and <= 200 ? q.Limit : 50;
        var items = await db.ListItemsAsync(p.Id, q.Status ?? "", q.Search?.Trim() ?? "", mine, uid, take, Math.Max(0, q.Offset), ct);
        var total = await db.CountItemsAsync(p.Id, q.Status ?? "", q.Search?.Trim() ?? "", mine, uid, ct);
        var ids = items.SelectMany(i => new[] { i.AnnotatorId, i.ReviewerId }).OfType<Guid>().Distinct().ToArray();
        var names = ids.Length == 0 ? [] : (await db.UserNamesAsync(ids, ct)).ToDictionary(u => u.Id, u => u.DisplayName);
        // Thumbnails for the work strip: signed links of the page's files (same sources as GetItem), one version listing per page.
        var urls = new Dictionary<string, string>();
        if (p.VersionId is { } vid && await db.GetVersionAsync(vid, p.WorkspaceId, ct) is { } v)
        {
            var wanted = items.Select(i => i.Path).ToHashSet();
            foreach (var f in (await db.ListVersionFilesAsync(vid, ct)).Where(f => wanted.Contains(f.Path)))
                urls[f.Path] = f.Url is { } u ? MLPlatform.ControlPlane.Application.Common.SourceUrls.Resolve(s3, u)
                    : s3.Presign(HttpMethod.Get, MLPlatform.ControlPlane.Application.Data.Buckets.Datasets, v.Prefix + f.Path, TimeSpan.FromHours(1));
        }
        else foreach (var i in items)
            urls[i.Path] = s3.Presign(HttpMethod.Get, MLPlatform.ControlPlane.Application.Data.Buckets.Datasets,
                MLPlatform.ControlPlane.Application.Data.Buckets.Raw(p.WorkspaceId, p.DatasetId) + i.Path, TimeSpan.FromHours(1));
        return new ItemPage(items, total, names, urls);
    }
}
