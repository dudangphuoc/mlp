using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

// ───────────── Resume from checkpoint ─────────────

/// <summary>
/// Picks the restore point for a new attempt (20/10 §4.6): latest checkpoint; same machine with a local copy → use the volume; different machine → durable copy (presigned GET).
/// Returns the value for `MLP_RESUME_FROM` (`local:&lt;name&gt;` | `url:&lt;presigned&gt;|&lt;name&gt;`) or null.
/// </summary>
public sealed record ResolveResumeCommand(Guid AttemptId, Guid JobId, Guid HostId) : ICommand<string?>, IInternalCommand;

public sealed class ResolveResumeCommandHandler(IWriteSession db, IClock clock, S3Client s3) : ICommandHandler<ResolveResumeCommand, string?>
{
    public async ValueTask<string?> Handle(ResolveResumeCommand c, CancellationToken ct)
    {
        var cps = await db.ListCheckpointsAsync(c.JobId, ct);
        if (cps.Count == 0) return null;
        var ws = await db.JobWorkspaceAsync(c.JobId, ct);
        // R28: every checkpoint name is restored together (D-FINE stage 2 reloads best_stg1.pth); the shim resumes from the recipe's first name.
        var local = cps.Where(x => x.Location == "local" && x.HostId == c.HostId).GroupBy(x => x.Name).Select(g => g.MaxBy(x => x.Step)!).ToList();
        var durable = cps.Where(x => x.Location == "durable" && x.S3Key is not null).GroupBy(x => x.Name).Select(g => g.MaxBy(x => x.Step)!).ToList();
        var localTop = local.Count > 0 ? local.Max(x => x.Step) : -1;
        var durableTop = durable.Count > 0 ? durable.Max(x => x.Step) : -1;
        string value, label;
        if (local.Count > 0 && localTop >= durableTop)
            (value, label) = (string.Join("\n", local.Select(x => $"local:{x.Name}")), $"step {localTop} (local copy on the same machine)");
        else if (durable.Count > 0)
            (value, label) = (string.Join("\n", durable.Select(x =>
                    $"url:{s3.Presign(HttpMethod.Get, Buckets.Artifacts, x.S3Key!, TimeSpan.FromHours(6), Audience.Workload)}|{x.Name}|{x.Sha256}")),
                $"step {durableTop} (durable copy in storage: {string.Join(", ", durable.Select(x => x.Name))})");
        else return null;
        await db.SetResumedFromAsync(c.AttemptId, label, ct);
        await TimelineEvents.EmitAsync(db, clock, ws, "scheduler", "attempt", c.AttemptId, "attempt.resumed", "info", "Resuming from checkpoint " + label, ct: ct);
        return value;
    }
}
