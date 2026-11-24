using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Who may write a run's result files: the job's container, or a member uploading for a code run.</summary>
internal static class ArtifactWriters
{
    public static async Task<(RunRow Run, Audience Audience)> RequireAsync(IReadSession db, TokenService tokens, WorkspaceAuthorizer auth,
        string? workloadToken, Guid runId, CancellationToken ct)
    {
        if (workloadToken is { Length: > 0 } t) return (await WorkloadAuth.RequireRunAsync(db, tokens, t, runId, ct), Audience.Workload);
        var run = await db.GetRunAsync(runId, ct) ?? throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
        await CodeRunRules.RequireAsync(db, auth, run.WorkspaceId, run.Id, ct);
        return (run, Audience.Public);
    }
}
