using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

internal static class LabelStore
{
    public static async Task ReplaceAsync(IWriteSession db, IClock clock, ProjectRow p, string path, Guid uid, IReadOnlyList<AnnotationInput> items, string action, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var before = await db.ListAnnotationsAsync(p.DatasetId, path, ct);
        await db.DeleteAnnotationsAsync(p.DatasetId, path, now, ct);
        foreach (var a in items)
            await db.InsertAnnotationAsync(Ids.New(), p.DatasetId, path, p.SchemaVersion, a.Kind, a.ClassId,
                string.IsNullOrEmpty(a.Geometry) ? "{}" : a.Geometry, string.IsNullOrEmpty(a.Attributes) ? "{}" : a.Attributes,
                string.IsNullOrEmpty(a.Source) ? "human" : a.Source, a.Confidence, uid, now, ct);
        await db.InsertHistoryAsync(Ids.New(), p.DatasetId, path, action,
            LabelRules.ToJson(before.Select(b => (b.Kind, b.ClassId, b.Geometry, b.Attributes, b.Source))),
            LabelRules.ToJson(items.Select(a => (a.Kind, a.ClassId, a.Geometry, a.Attributes, a.Source))), uid, now, ct);
        await db.BumpRevisionAsync(p.Id, path, ct);
    }
}
