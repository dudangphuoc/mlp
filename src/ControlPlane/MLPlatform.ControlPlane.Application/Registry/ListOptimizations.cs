using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Optimization runs of the workspace, newest first; running ones are settled from their job first.</summary>
[RequiresPermission("run.read")]
public sealed record ListOptimizationsCommand(Guid WorkspaceId) : ICommand<IReadOnlyList<OptimizationRow>>;

/// <summary>A finished job's report.json becomes the run's report (ready when at least one candidate was
/// built).</summary>
public sealed class ListOptimizationsCommandHandler(IWriteSession db, S3Client s3, WorkspaceAuthorizer auth) : ICommandHandler<ListOptimizationsCommand, IReadOnlyList<OptimizationRow>>
{
    public async ValueTask<IReadOnlyList<OptimizationRow>> Handle(ListOptimizationsCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        var rows = await db.ListOptimizationsAsync(c.WorkspaceId, ct);
        var changed = false;
        foreach (var o in rows.Where(o => o.State == "running")) changed |= await Settle(db, s3, o, ct);
        return changed ? await db.ListOptimizationsAsync(c.WorkspaceId, ct) : rows;
    }

    /// <summary>Settles one running run from its job; true when the row changed.</summary>
    public static async Task<bool> Settle(IWriteSession db, S3Client s3, OptimizationRow o, CancellationToken ct)
    {
        var job = o.JobId is { } jid ? await db.GetJobAsync(jid, o.WorkspaceId, ct) : null;
        if (job is null) { await db.FinishOptimizationAsync(o.WorkspaceId, o.Id, "failed", "The job is gone", "{}", ct); return true; }
        if (job.State is not ("succeeded" or "failed" or "cancelled")) return false;
        JsonObject? report = null;
        try { report = await s3.GetAsync(Buckets.Artifacts, OptimizationRules.Prefix(o.WorkspaceId, o.Id) + "report.json", ct) is { } raw ? JsonNode.Parse(raw) as JsonObject : null; }
        catch (JsonException) { }
        var ok = report?["state"]?.ToString() == "ready";
        var error = ok ? "" : report?["error"]?.ToString() ?? (job.State == "cancelled" ? "cancelled" : job.StateReason ?? job.LastExitReason ?? "The job failed");
        await db.FinishOptimizationAsync(o.WorkspaceId, o.Id, ok ? "ready" : "failed", error, report?.ToJsonString() ?? "{}", ct);
        return true;
    }
}
