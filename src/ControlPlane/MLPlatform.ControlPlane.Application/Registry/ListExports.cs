using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>The conversions of a model version, newest first; running ones are settled from their job first.</summary>
[RequiresPermission("run.read")]
public sealed record ListExportsCommand(Guid WorkspaceId, Guid ModelVersionId) : ICommand<IReadOnlyList<ExportRow>>;

/// <summary>A running conversion whose job succeeded becomes ready (the uploaded file takes its real name), one whose
/// job failed or was cancelled becomes failed with the job's reason.</summary>
public sealed class ListExportsCommandHandler(IWriteSession db, S3Client s3, WorkspaceAuthorizer auth) : ICommandHandler<ListExportsCommand, IReadOnlyList<ExportRow>>
{
    public async ValueTask<IReadOnlyList<ExportRow>> Handle(ListExportsCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        var rows = await db.ListExportsAsync(c.WorkspaceId, c.ModelVersionId, ct);
        var changed = false;
        foreach (var e in rows.Where(e => e.State == "running"))
        {
            var job = e.JobId is { } jid ? await db.GetJobAsync(jid, c.WorkspaceId, ct) : null;
            if (job is null) { await db.FinishExportAsync(c.WorkspaceId, e.Id, "failed", "The job is gone", "", 0, ct); changed = true; continue; }
            if (job.State is "failed" or "cancelled")
            {
                await db.FinishExportAsync(c.WorkspaceId, e.Id, "failed", job.StateReason ?? job.LastExitReason ?? job.State, "", 0, ct);
                changed = true;
            }
            else if (job.State == "succeeded")
            {
                var prefix = ExportRules.Prefix(c.WorkspaceId, e.Id);
                JsonObject? meta = null;
                try { meta = await s3.GetAsync(Buckets.Artifacts, prefix + ExportRules.Meta, ct) is { } raw ? JsonNode.Parse(raw) as JsonObject : null; }
                catch (JsonException) { }
                var file = Path.GetFileName(meta?["file"]?.ToString() ?? "");
                if (file.Length == 0) await db.FinishExportAsync(c.WorkspaceId, e.Id, "failed", "The job finished without a result file", "", 0, ct);
                else
                {
                    await s3.CopyAsync(Buckets.Artifacts, prefix + ExportRules.Staged, Buckets.Artifacts, prefix + file, ct);
                    await s3.DeleteAsync(Buckets.Artifacts, prefix + ExportRules.Staged, ct);
                    await db.FinishExportAsync(c.WorkspaceId, e.Id, "ready", "", file, InspectionRules.Long(meta?["size"]), ct);
                }
                changed = true;
            }
        }
        return changed ? await db.ListExportsAsync(c.WorkspaceId, c.ModelVersionId, ct) : rows;
    }
}
