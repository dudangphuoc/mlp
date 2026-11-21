using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Log metrics: a user with run write permission, or the container of the same job (workload token).</summary>
public sealed record LogMetricsCommand(Guid RunId, string? WorkloadToken, IReadOnlyList<(string Key, long Step, double Value, DateTimeOffset? At)> Points)
    : ICommand<int>;

public sealed class LogMetricsCommandHandler(IWriteSession db, IClock clock, TokenService tokens, WorkspaceAuthorizer auth)
    : ICommandHandler<LogMetricsCommand, int>
{
    public async ValueTask<int> Handle(LogMetricsCommand c, CancellationToken ct)
    {
        var run = await db.GetRunAsync(c.RunId, ct) ?? throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
        if (c.WorkloadToken is { Length: > 0 } t)
        {
            var a = await MLPlatform.ControlPlane.Application.Scheduling.Workload.ResolveAsync(db, tokens, t, ct);   // fencing (20/10 §4.5)
            if (a.JobId != run.JobId) throw new UnauthenticatedException("SCHED_TOKEN_INVALID", "Job token is not valid for this run");
        }
        else await auth.RequireAsync(run.WorkspaceId, "job.submit", ct);
        if (c.Points.Count > 10_000) throw new ResourceExhaustedException("TRACK_BATCH_TOO_LARGE", "At most 10,000 points per batch");
        double? lastPrimary = null;
        long lastStep = long.MinValue;
        foreach (var p in c.Points)
        {
            if (!double.IsFinite(p.Value)) continue;
            await db.InsertMetricAsync(run.Id, p.Key, p.Step, p.At ?? clock.UtcNow, p.Value, ct);
            if (p.Key == run.PrimaryMetricKey && p.Step >= lastStep) { lastPrimary = p.Value; lastStep = p.Step; }
        }
        if (lastPrimary is { } v) await db.SetPrimaryMetricAsync(run.Id, v, ct);
        if (run.State == "queued") await db.SetRunStateAsync(run.Id, "running", clock.UtcNow, ct);
        return c.Points.Count;
    }
}
