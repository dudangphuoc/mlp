using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

internal static class WorkloadAuth
{
    public static async Task<RunRow> RequireRunAsync(IReadSession db, TokenService tokens, string token, Guid runId, CancellationToken ct)
    {
        var run = await db.GetRunAsync(runId, ct) ?? throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
        var a = await MLPlatform.ControlPlane.Application.Scheduling.Workload.ResolveAsync(db, tokens, token, ct);
        if (a.JobId != run.JobId) throw new UnauthenticatedException("SCHED_TOKEN_INVALID", "Job token is not valid for this run");
        return run;
    }
}
