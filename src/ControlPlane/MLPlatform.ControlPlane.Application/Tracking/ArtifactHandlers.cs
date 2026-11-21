using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

public static class RunArtifacts
{
    public static string Prefix(Guid ws, Guid run) => $"{ws}/runs/{run}/model/";
}

/// <summary>Container (workload token of the same job) requests presigned PUTs for files in the model directory (20/08 §5.1 step 7).</summary>
public sealed record CreateArtifactUploadsCommand(string WorkloadToken, Guid RunId, IReadOnlyList<string> Paths) : ICommand<IReadOnlyList<string>>;

public sealed class CreateArtifactUploadsCommandHandler(IReadSession db, S3Client s3, TokenService tokens)
    : ICommandHandler<CreateArtifactUploadsCommand, IReadOnlyList<string>>
{
    public async ValueTask<IReadOnlyList<string>> Handle(CreateArtifactUploadsCommand c, CancellationToken ct)
    {
        var run = await WorkloadAuth.RequireRunAsync(db, tokens, c.WorkloadToken, c.RunId, ct);
        if (c.Paths.Count > 10_000) throw new ResourceExhaustedException("TRACK_TOO_MANY_FILES", "Too many artifact files");
        return c.Paths.Select(p => s3.Presign(HttpMethod.Put, Buckets.Artifacts, RunArtifacts.Prefix(run.WorkspaceId, run.Id) + Buckets.SafePath(p),
            TimeSpan.FromHours(6), Audience.Workload)).ToList();
    }
}

public sealed record CompleteArtifactsCommand(string WorkloadToken, Guid RunId, IReadOnlyList<(string Path, long Size)> Files) : ICommand<int>;

public sealed class CompleteArtifactsCommandHandler(IWriteSession db, TokenService tokens) : ICommandHandler<CompleteArtifactsCommand, int>
{
    public async ValueTask<int> Handle(CompleteArtifactsCommand c, CancellationToken ct)
    {
        var run = await WorkloadAuth.RequireRunAsync(db, tokens, c.WorkloadToken, c.RunId, ct);
        foreach (var (path, size) in c.Files) await db.UpsertRunArtifactAsync(run.Id, Buckets.SafePath(path), size, ct);
        return c.Files.Count;
    }
}

internal static class WorkloadAuth
{
    public static async Task<RunRow> RequireRunAsync(IReadSession db, TokenService tokens, string token, Guid runId, CancellationToken ct)
    {
        var run = await db.GetRunAsync(runId, ct) ?? throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
        var a = await MLPlatform.ControlPlane.Application.Ops.Workload.ResolveAsync(db, tokens, token, ct);
        if (a.JobId != run.JobId) throw new UnauthenticatedException("SCHED_TOKEN_INVALID", "Job token is not valid for this run");
        return run;
    }
}

/// <summary>Internal for M11: artifacts + final metrics of a run (no permission check — the caller already checked).</summary>
public sealed record GetRunForRegistryQuery(Guid RunId) : IQuery<(RunRow Run, IReadOnlyList<ArtifactRow> Artifacts, IReadOnlyList<FinalMetricRow> Metrics)?>;

public sealed class GetRunForRegistryQueryHandler(IReadSession db)
    : IQueryHandler<GetRunForRegistryQuery, (RunRow Run, IReadOnlyList<ArtifactRow> Artifacts, IReadOnlyList<FinalMetricRow> Metrics)?>
{
    public async ValueTask<(RunRow Run, IReadOnlyList<ArtifactRow> Artifacts, IReadOnlyList<FinalMetricRow> Metrics)?> Handle(GetRunForRegistryQuery q, CancellationToken ct)
    {
        if (await db.GetRunAsync(q.RunId, ct) is not { } run) return null;
        return (run, await db.ListRunArtifactsAsync(run.Id, ct), await db.FinalMetricsAsync(run.Id, ct));
    }
}
