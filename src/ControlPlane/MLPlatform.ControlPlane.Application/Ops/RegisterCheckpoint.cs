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

public sealed record RegisterCheckpointCommand(string Token, string Name, long Step, string Location, string? Key, long SizeBytes, string Sha256)
    : ICommand<bool>, IInternalCommand;

public sealed class RegisterCheckpointCommandHandler(IWriteSession db, IClock clock, TokenService tokens, S3Client s3) : ICommandHandler<RegisterCheckpointCommand, bool>
{
    public async ValueTask<bool> Handle(RegisterCheckpointCommand c, CancellationToken ct)
    {
        var a = await Workload.ResolveAsync(db, tokens, c.Token, ct);
        if (c.Location is not ("local" or "durable")) throw new ValidationException(new Dictionary<string, string> { ["location"] = "local | durable" });
        if (c.Location == "durable" && (c.Key is null || !c.Key.StartsWith($"{a.WorkspaceId}/jobs/{a.JobId}/checkpoints/", StringComparison.Ordinal)))
            throw new ForbiddenException("SCHED_CHECKPOINT_KEY_INVALID", "Checkpoint key does not belong to this job");
        var replaced = c.Location == "durable"
            ? (await db.ListCheckpointsAsync(a.JobId, ct)).FirstOrDefault(x => x.Location == "durable" && x.Name == Buckets.SafePath(c.Name) && x.S3Key != c.Key)
            : null;
        await db.InsertCheckpointAsync(Ids.New(), a.JobId, a.Id, a.HostIds[0], c.Step, Buckets.SafePath(c.Name), c.Location, c.Key, c.SizeBytes, c.Sha256, clock.UtcNow, ct);
        if (replaced?.S3Key is { } oldKey) await s3.DeleteAsync(Buckets.Artifacts, oldKey, ct);
        if (c.Location == "durable")
            await TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, "shim", "attempt", a.Id, "checkpoint.durable", "success",
                $"Checkpoint at step {c.Step} saved durably ({c.SizeBytes / 1024.0:0.#} KB)", new JsonObject { ["step"] = c.Step, ["name"] = c.Name }, ct: ct);
        if (c.Location == "durable") await CheckpointRetention.PruneAsync(db, s3, a.JobId, ct);
        return true;
    }
}
