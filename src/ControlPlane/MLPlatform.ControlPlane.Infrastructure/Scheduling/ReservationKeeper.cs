using System.Text.Json;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Ray;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Workloads outlive the Ray head (D-40), but their reservation does not: after a head restart Ray believes
/// every CPU and GPU is free and would give a busy GPU to the next job.</summary>
public sealed class ReservationKeeper(IServiceScopeFactory scopes, RayJobsClient ray, IConfiguration cfg, ILogger<ReservationKeeper> log)
{
    private readonly Dictionary<Guid, int> _tries = [];
    private long _checkedAt, _settleFrom;
    private bool _ok;
    private const int MaxReholdTries = 5;

    /// <summary>True when new jobs may be placed.</summary>
    public async Task<bool> EnsureAsync(CancellationToken ct)
    {
        var time = TimeProvider.System;
        if (_settleFrom != 0 && time.GetElapsedTime(_settleFrom) < TimeSpan.FromSeconds(20)) return false;
        if (_checkedAt != 0 && time.GetElapsedTime(_checkedAt) < TimeSpan.FromSeconds(10)) return _ok;
        _checkedAt = time.GetTimestamp();
        try
        {
            if (!await ray.PingAsync(ct)) return _ok = false;
            await using var scope = scopes.CreateAsyncScope();
            var m = scope.ServiceProvider.GetRequiredService<IMediator>();
            var active = (await m.Query(new ListActiveAttemptsQuery(), ct)).Where(a => a.State is "running" or "suspect").ToList();
            foreach (var gone in _tries.Keys.Where(k => active.All(a => a.Id != k)).ToList()) _tries.Remove(gone);
            var reheld = false;
            foreach (var a in active)
                if (await RecheckAsync(m, a, ct)) reheld = true;
            if (reheld) { _settleFrom = time.GetTimestamp(); return _ok = false; }
            return _ok = true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Ray is unreachable — scheduling paused: {Msg}", e.Message);
            return _ok = false;
        }
    }

    /// <summary>Re-holds the attempt's reservation when Ray lost it; true when a re-hold was submitted.</summary>
    private async Task<bool> RecheckAsync(IMediator m, AttemptRow a, CancellationToken ct)
    {
        var info = await ray.GetAsync(a.Id.ToString(), ct);
        if (info?.Status is "PENDING" or "RUNNING") return false;
        var n = _tries.GetValueOrDefault(a.Id);
        if (n >= MaxReholdTries) return false;
        _tries[a.Id] = n + 1;
        if (info is not null) await ray.DeleteAsync(a.Id.ToString(), ct);
        var spec = JobSpecDto.Parse(a.Spec);
        var online = await m.Query(new ListOnlineHostsQuery(a.WorkspaceId), ct);
        var needMb = GpuNeed.Mb(spec);
        var held = (await m.Query(new ListAttemptAllocationsQuery(a.Id), ct))
            .Select(x => new HeldBundleDto
            {
                HostId = x.HostId, Cpus = spec.Cpus, MemoryBytes = spec.MemoryBytes, GpuUuids = x.GpuUuids,
                GpuFraction = GpuNeed.Fraction(needMb, SchedulerLoop.GpuTotalMb(online.FirstOrDefault(h => h.Id == x.HostId))),
            }).ToList();
        if (held.Count == 0) return false;
        var entry = string.Join(' ', "python", cfg["Scheduler:HolderPath"] ?? "/opt/mlp/mlp_holder/holder.py", "--rehold", "--attempt", a.Id.ToString(),
            "--bundles", "'" + JsonSerializer.Serialize(held, SchedJson.Default.ListHeldBundleDto) + "'");
        await ray.SubmitAsync(a.Id.ToString(), entry, new Dictionary<string, string> { ["mlp_job"] = a.JobId.ToString(), ["mlp_rehold"] = "1" }, ct);
        await m.Send(new EmitAttemptEventCommand(a.WorkspaceId, a.Id, "attempt.reheld", "warning",
            "The cluster head restarted and lost this reservation — CPU, memory and GPU reserved again"), ct);
        log.LogWarning("Attempt {Attempt}: reservation missing in Ray ({Status}) → re-held", a.Id, info?.Status ?? "unknown job");
        return true;
    }
}
