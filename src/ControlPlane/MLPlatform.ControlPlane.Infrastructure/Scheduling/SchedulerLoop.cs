using System.Text.Json;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Ray;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>
/// M03 §5 scheduling loop (2 s): take the highest-priority job → pick hosts → create attempt + `mlpj_` token →
/// submit Ray job `mlp_holder` to create the placement group (R1). When the holder reports it is fully allocated → <see cref="WorkloadLauncher"/>.
/// </summary>
public sealed class SchedulerLoop(IServiceScopeFactory scopes, RayJobsClient ray, IConfiguration cfg, SystemHealth health, ReservationKeeper keeper, ILogger<SchedulerLoop> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!string.Equals(cfg["Scheduler:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
        {
            log.LogInformation("Scheduler disabled (MLP__Scheduler__Enabled != true)");
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stop))
        {
            health.Beat("scheduler", 2);
            try
            {
                // Running workloads must hold their resources in Ray before anything new is placed (the head forgets them when it restarts).
                if (!await keeper.EnsureAsync(stop)) continue;
                while (await TickAsync(stop)) { }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Scheduling loop failed");
            }
        }
    }

    private async Task<bool> TickAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        var claimed = await m.Send(new ClaimNextJobCommand(), ct);
        if (claimed is null) return false;
        var (job, spec) = (claimed.Job, claimed.Spec);

        List<Guid> hosts;
        var online = await m.Query(new ListOnlineHostsQuery(job.WorkspaceId), ct);
        if (spec.Hosts.Count > 0) hosts = spec.Hosts;
        else
        {
            IReadOnlyList<Guid> candidates = online.Where(h => Fits(h, spec)).Select(h => h.Id).ToList();
            // Host dataset cache: prefer the machine that already holds the job's dataset (only what changed is downloaded there).
            if (candidates.Count > spec.Nnodes && spec.Env.TryGetValue("MLP_JOB_SPEC", out var js)
                && System.Text.Json.Nodes.JsonNode.Parse(js)?["datasetId"]?.ToString() is { } dsText && Guid.TryParse(dsText, out var dsId))
                candidates = await m.Send(new MLPlatform.ControlPlane.Application.Ops.PreferDataHostsCommand(job.WorkspaceId, job.Id, dsId, candidates), ct);
            var fit = candidates.Take(spec.Nnodes).ToList();
            if (fit.Count < spec.Nnodes)
            {
                await m.Send(new RequeueJobCommand(job.Id, $"Waiting for hosts: need {spec.Nnodes} host(s) with {spec.Cpus} CPU, {spec.Gpus} GPU"), ct);
                return true;   // this job backs off 5 s (not_before); try the next job right away in this loop
            }
            hosts = fit;
        }
        // D-50: a one-GPU job reserves its memory budget on the GPU, not the device — other jobs fit beside it while budgets allow.
        var needMb = GpuNeed.Mb(spec);
        var bundles = hosts.Select(h => new BundleDto
        {
            HostId = h, Cpus = spec.Cpus, Gpus = spec.Gpus, MemoryBytes = spec.MemoryBytes,
            GpuFraction = GpuNeed.Fraction(needMb, GpuTotalMb(online.FirstOrDefault(x => x.Id == h))),
        }).ToList();
        var attempt = await m.Send(new CreateAttemptCommand(job.Id, job.AttemptCount, bundles), ct);
        var api = cfg["Scheduler:ApiAddressForRay"] ?? "10.88.0.1:5080";
        var entry = string.Join(' ', "python", cfg["Scheduler:HolderPath"] ?? "/opt/mlp/mlp_holder/holder.py",
            "--attempt", attempt.AttemptId.ToString(), "--api", api, "--token", attempt.Token,
            "--bundles", "'" + JsonSerializer.Serialize(bundles, SchedJson.Default.ListBundleDto) + "'",
            "--strategy", spec.Nnodes > 1 ? "STRICT_SPREAD" : "STRICT_PACK");
        try
        {
            await ray.SubmitAsync(attempt.AttemptId.ToString(), entry, new Dictionary<string, string> { ["mlp_job"] = job.Id.ToString() }, ct);
            log.LogInformation("Job {Job} attempt {Attempt} → placement group on {Hosts}", job.Id, attempt.AttemptId, string.Join(",", hosts));
        }
        catch (HttpRequestException e)
        {
            log.LogWarning("Ray rejected the job: {Msg}", e.Message);
            var fin = await m.Send(new FinishAttemptCommand(attempt.AttemptId, 1, "ray_unavailable", e.Message), ct);
            // Rejected before any container started: the deployment / notebook / run must not stay "starting" forever.
            await m.Send(new MLPlatform.ControlPlane.Application.Serving.SetDeploymentStateByJobCommand(fin.JobId,
                fin.JobState == "queued" ? "deploying" : "failed", e.Message, null), ct);
            await m.Send(new MLPlatform.ControlPlane.Application.Notebooks.SetNotebookStateByJobCommand(fin.JobId,
                fin.JobState == "queued" ? "starting" : "failed", e.Message, null, null), ct);
            await m.Send(new MLPlatform.ControlPlane.Application.Tracking.SyncRunStateCommand(fin.JobId, fin.JobState), ct);
        }
        return true;
    }

    /// <summary>Memory of the host's smallest GPU in MB (0 when unknown) — the base a job's GPU share is computed against.</summary>
    internal static long GpuTotalMb(HostRow? h)
    {
        if (h?.HostInfo is not { Length: > 0 } b) return 0;
        var gpus = HostInfo.Parser.ParseFrom(b).Gpus;
        return gpus.Count == 0 ? 0 : (long)(gpus.Min(g => g.MemoryBytes) >> 20);
    }

    /// <summary>Filter by real hardware from HostInfo (D-12): enough logical CPUs and GPUs.</summary>
    private static bool Fits(HostRow h, JobSpecDto spec)
    {
        if (h.HostInfo is not { Length: > 0 } b) return spec.Gpus == 0;
        var info = HostInfo.Parser.ParseFrom(b);
        return info.LogicalCpus >= spec.Cpus && info.Gpus.Count >= spec.Gpus;
    }
}
