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

// ───────────── Label set ─────────────

/// <summary>Commits a label set (§5 step 6): an immutable JSONL snapshot in S3 storage (`…/labelsets/{id}.jsonl`). Parquet later.</summary>
public sealed record CreateLabelSetCommand(Guid WorkspaceId, Guid DatasetId, bool AllLabeled) : ICommand<LabelSetRow>;

public sealed class CreateLabelSetCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, S3Client s3) : ICommandHandler<CreateLabelSetCommand, LabelSetRow>
{
    public async ValueTask<LabelSetRow> Handle(CreateLabelSetCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var schema = await db.GetCurrentSchemaAsync(c.DatasetId, ct) ?? throw new PreconditionException("LABEL_SCHEMA_REQUIRED", "Dataset has no label schema yet");
        var rows = await db.LabelSetRowsAsync(c.DatasetId, c.AllLabeled, ct);
        if (rows.Count == 0) throw new PreconditionException("LABEL_SET_EMPTY", "No eligible labels to commit yet");
        using var ms = new MemoryStream();
        foreach (var r in rows)
        {
            using (var w = new Utf8JsonWriter(ms)) LabelRules.WriteOne(w, r.Path, r.Kind, r.ClassId, r.Geometry, r.Attributes, r.Source);
            ms.WriteByte((byte)'\n');
        }
        var id = Ids.New();
        var key = $"{c.WorkspaceId}/datasets/{c.DatasetId}/labelsets/{id}.jsonl";
        await s3.PutAsync(Buckets.Datasets, key, ms.ToArray(), "application/x-ndjson", ct);
        var mode = c.AllLabeled ? "all_labeled" : "accepted_only";
        var items = rows.Select(r => r.Path).Distinct().LongCount();
        var now = clock.UtcNow;
        var n = await db.InsertLabelSetAsync(id, c.DatasetId, schema.Version, key, items, rows.Count, mode, uid, now, ct);
        return new LabelSetRow(id, n, schema.Version, items, rows.Count, mode, now);
    }
}
