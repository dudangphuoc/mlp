using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Loads an item's context once the caller is authorized (shared by user requests and background jobs).</summary>
public static class LabelContextLoader
{
    public static async Task<HermesContext> LoadAsync(IReadSession db, ProjectRow p, string path, Guid actor, CancellationToken ct)
    {
        var s = await db.GetItemStateAsync(p.Id, path, ct) ?? throw new NotFoundException("LABEL_NOT_ASSIGNED", "Item not found");
        var schema = await db.GetSchemaVersionAsync(p.DatasetId, p.SchemaVersion, ct);
        HermesSource src;
        if (p.VersionId is { } vid)
        {
            var v = await db.GetVersionAsync(vid, p.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
            var f = await db.GetVersionFileAsync(vid, path, ct);
            src = f?.Url is { } u ? new HermesSource(null, null, u) : new HermesSource(Buckets.Datasets, v.Prefix + path, null);
        }
        else src = new HermesSource(Buckets.Datasets, Buckets.Raw(p.WorkspaceId, p.DatasetId) + path, null);
        var ds = await db.GetDatasetAsync(p.DatasetId, p.WorkspaceId, ct);
        return new HermesContext(actor, p, s, schema?.Classes ?? "[]", await db.ListAnnotationsAsync(p.DatasetId, path, ct), src, ds?.Task ?? "");
    }
}
