using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Keep the job's 3 latest durable checkpoints (20/10 §4.2); older ones are deleted from storage.</summary>
internal static class CheckpointRetention
{
    public static async Task PruneAsync(IWriteSession db, S3Client s3, Guid jobId, CancellationToken ct)
    {
        foreach (var old in (await db.ListCheckpointsAsync(jobId, ct)).Where(c => c.Location == "durable").OrderByDescending(c => c.Step).Skip(3))
        {
            if (old.S3Key is { } k) await s3.DeleteAsync(Data.Buckets.Artifacts, k, ct);
            await db.DeleteCheckpointAsync(old.Id, ct);
        }
    }
}
