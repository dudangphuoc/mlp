using System.Text.Json;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Ray;

namespace MLPlatform.Api.Infrastructure;

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

/// <summary>
/// Workloads outlive the Ray head (D-40), but their reservation does not: after a head restart Ray believes every CPU and GPU is free and would
/// give a busy GPU to the next job. Every 10 s this compares active attempts with Ray and submits a `--rehold` holder for each one Ray no longer
/// knows. Scheduling pauses while Ray is unreachable and for a short settle time after a re-hold.
/// </summary>
public sealed class ReservationKeeper(IServiceScopeFactory scopes, RayJobsClient ray, IConfiguration cfg, ILogger<ReservationKeeper> log)
{
    private readonly Dictionary<Guid, int> _tries = [];
    private long _checkedAt, _settleFrom;
    private bool _ok;

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
            {
                var info = await ray.GetAsync(a.Id.ToString(), ct);
                if (info?.Status is "PENDING" or "RUNNING") continue;
                var n = _tries.GetValueOrDefault(a.Id);
                if (n >= 5) continue;   // the holder keeps dying: leave it visible in the log instead of resubmitting forever
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
                if (held.Count == 0) continue;
                var entry = string.Join(' ', "python", cfg["Scheduler:HolderPath"] ?? "/opt/mlp/mlp_holder/holder.py", "--rehold", "--attempt", a.Id.ToString(),
                    "--bundles", "'" + JsonSerializer.Serialize(held, SchedJson.Default.ListHeldBundleDto) + "'");
                await ray.SubmitAsync(a.Id.ToString(), entry, new Dictionary<string, string> { ["mlp_job"] = a.JobId.ToString(), ["mlp_rehold"] = "1" }, ct);
                await m.Send(new EmitAttemptEventCommand(a.WorkspaceId, a.Id, "attempt.reheld", "warning",
                    "The cluster head restarted and lost this reservation — CPU, memory and GPU reserved again"), ct);
                log.LogWarning("Attempt {Attempt}: reservation missing in Ray ({Status}) → re-held", a.Id, info?.Status ?? "unknown job");
                reheld = true;
            }
            if (reheld) { _settleFrom = time.GetTimestamp(); return _ok = false; }
            return _ok = true;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Ray is unreachable — scheduling paused: {Msg}", e.Message);
            return _ok = false;
        }
    }
}

/// <summary>Fully allocated → send StartWorkload to the agent on each host (20/08 §9 step 3).</summary>
public sealed class WorkloadLauncher(AgentRegistry agents, IServiceScopeFactory scopes, RayJobsClient ray, IConfiguration cfg, RecipeEnvironments envs, LogStore logs, ILogger<WorkloadLauncher> log)
{
    public async Task LaunchAsync(AllocationComplete done, CancellationToken ct)
    {
        var a = done.Attempt;
        var spec = done.Spec;
        var rank = 0;
        string workloadToken;
        Guid? runId;
        await using (var s0 = scopes.CreateAsyncScope())
        {
            var med = s0.ServiceProvider.GetRequiredService<IMediator>();
            workloadToken = await med.Send(new MLPlatform.ControlPlane.Application.Tracking.MintWorkloadTokenCommand(a.Id), ct);
            runId = await med.Query(new MLPlatform.ControlPlane.Application.Tracking.GetRunIdForJobQuery(a.JobId), ct);
        }
        foreach (var alloc in done.Allocations)
        {
            if (!agents.TryGet(alloc.HostId, out var session))
            {
                await FailAsync(a.Id, "node_lost", $"Host {alloc.HostId} was offline at launch", ct);
                return;
            }
            // D-38: the control plane assembles the container spec; the agent only runs it. --init: the workload process is not PID 1 (which would ignore SIGTERM).
            var nodeRank = rank++;
            var sw = new ContainerSpec
            {
                Name = HostContainers.WorkloadName(a.Id.ToString(), nodeRank), Image = spec.Image, Cpus = spec.Cpus, MemoryBytes = (ulong)spec.MemoryBytes, Init = true,
                Network = !string.IsNullOrEmpty(spec.Network) ? spec.Network : Overlay.HubPublicKey(cfg) is not null ? Overlay.ContainerNetwork : cfg["Scheduler:DefaultWorkloadNetwork"] ?? "",
            };
            sw.Labels["mlp.role"] = "workload";
            sw.Labels["mlp.attempt"] = a.Id.ToString();
            sw.Labels["mlp.logs"] = a.Id.ToString();
            sw.Gpus.AddRange(alloc.GpuUuids);
            var servePort = 0;
            var isServe = spec.Env.ContainsKey("MLP_DEPLOYMENT");
            var isNotebook = spec.Env.TryGetValue("MLP_NOTEBOOK", out _);
            if (isServe)
            {
                servePort = 8500 + Random.Shared.Next(100);   // 8500–8599 (20/01 §2), chung namespace mlp-net
                if (string.Equals(cfg["Serving:PublishToHost"], "true", StringComparison.OrdinalIgnoreCase))   // dev only: the overlay replaces this in production
                    sw.Ports.Add(new PortBinding { HostIp = "127.0.0.1", ContainerPort = servePort });
                sw.Env["MLP_SERVE_PORT"] = servePort.ToString(System.Globalization.CultureInfo.InvariantCulture);
                sw.Env["MLP_DEVICE"] = alloc.GpuUuids.Length > 0 ? "cuda" : "cpu";   // a GPU deployment must not silently run on CPU
            }
            // Builtin recipe image (recipes/<name>:current) not on the host yet → build it there: recipe Dockerfile + serve layer + training layer
            // (mlp-shim for the host's architecture); reused afterwards. Serving and training share the image.
            if (!isNotebook && HostContainers.RecipeImage(spec.Image, cfg["Catalog:RecipesDir"]) is not null)
            {
                var tag = await PrepareRecipeImageAsync(a, alloc.HostId, session, spec.Image, isServe, ct);
                if (tag is null) return;
                sw.Image = tag;
            }
            if (spec.Env.TryGetValue("MLP_SHM_GB", out var shmGb) && ulong.TryParse(shmGb, out var gb) && gb > 0) sw.ShmBytes = gb << 30;   // DataLoader workers share /dev/shm
            if (isNotebook)
            {
                // R2: the Jupyter port is bound to the host's 127.0.0.1 only; the api reaches it over the data channel. Persistent home = per-user volume on the host.
                sw.Ports.Add(new PortBinding { HostIp = "127.0.0.1", ContainerPort = MLPlatform.ControlPlane.Application.Notebooks.NotebookRules.ContainerPort });
                sw.Mounts.Add(new Mount { Source = spec.Env["MLP_HOME_VOLUME"], Target = MLPlatform.ControlPlane.Application.Notebooks.NotebookRules.HomeMount });
                // Published ports cannot share the mlp-net namespace (Docker forbids it) → host bridge network; notebooks do not need the overlay.
                if (string.IsNullOrEmpty(spec.Network)) sw.Network = cfg["Scheduler:PublishNetwork"] ?? "";
                // Profile image not on the host yet → build it there from the control plane's Dockerfile (a few minutes the first time, reused afterwards).
                if (HostContainers.NotebookImage(spec.Image) is { } build)
                {
                    await using (var sb = scopes.CreateAsyncScope())
                        await sb.ServiceProvider.GetRequiredService<IMediator>().Send(new MLPlatform.ControlPlane.Application.Notebooks.SetNotebookStateByJobCommand(
                            a.JobId, "starting", "Preparing the notebook image on the host (the first time may take a few minutes)", alloc.HostId, null), ct);
                    var br = await session.SendCommandAsync(new AgentCommand { ImageBuild = build }, TimeSpan.FromMinutes(45), ct);
                    if (!br.Ok) { await FailAsync(a.Id, br.ErrorReason, "Notebook image build failed: " + br.Message, ct); return; }
                }
            }
            sw.Command.AddRange(spec.Command);
            foreach (var (k, v) in spec.Env) sw.Env[k] = v;
            if (isServe && spec.Env.TryGetValue("MLP_MODEL_VERSION", out var mv) && Guid.TryParse(mv, out var modelVersion))
            {
                // Model links last 24 hours: signed again at every start. The files stay on the host in a per-version volume, so a restart does not download them again.
                await using var sm = scopes.CreateAsyncScope();
                sw.Env["MLP_MODEL_FILES"] = await sm.ServiceProvider.GetRequiredService<IMediator>().Query(
                    new MLPlatform.ControlPlane.Application.Serving.ResolveModelFilesQuery(a.WorkspaceId, modelVersion), ct);
                sw.Mounts.Add(new Mount { Source = $"mlp-model-{modelVersion:N}", Target = "/mnt/model" });
            }
            // Jobs that read dataset images directly (optimize): mount the named host dataset caches.
            if (spec.Env.TryGetValue("MLP_DATA_CACHES", out var caches))
                foreach (var c in caches.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    if (Guid.TryParse(c, out var cds))
                        sw.Mounts.Add(new Mount { Source = MLPlatform.ControlPlane.Application.Data.DataCacheRules.Volume(cds), Target = MLPlatform.ControlPlane.Application.Data.DataCacheRules.Mount(cds) });
            sw.Env["MLP_ATTEMPT_ID"] = a.Id.ToString();
            sw.Env["MLP_NODE_RANK"] = nodeRank.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sw.Env["MLP_NNODES"] = done.Allocations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sw.Env["MLP_MASTER_ADDR"] = done.Allocations[0].NodeIp;
            sw.Env["MLP_JOB_TOKEN"] = workloadToken;
            using (var act = MLPlatform.Cqrs.Pipeline.Source.StartActivity("LaunchAttempt")) if (act?.Id is { } tp) sw.Env["TRACEPARENT"] = tp;   // trace spans api → Ray job (20/07)
            sw.Env["MLP_API"] = cfg["Scheduler:ApiAddressForWorkloads"] ?? cfg["Scheduler:ApiAddressForRay"] ?? "10.88.0.1:5080";
            if (runId is { } rid)
            {
                sw.Env["MLP_RUN_ID"] = rid.ToString();
                // D-36: output lives on a per-run volume (local checkpoints survive container death/host reboot) + a restore point for later attempts.
                sw.Mounts.Add(new Mount { Source = $"mlp-run-{rid:N}", Target = "/mnt/output" });
                sw.Labels["mlp.volume"] = $"mlp-run-{rid:N}";   // removed once the job is fully finished (no retries left)
                await using var sr = scopes.CreateAsyncScope();
                var resume = await sr.ServiceProvider.GetRequiredService<IMediator>().Send(
                    new MLPlatform.ControlPlane.Application.Ops.ResolveResumeCommand(a.Id, a.JobId, alloc.HostId), ct);
                if (resume is not null) sw.Env["MLP_RESUME_FROM"] = resume;
                // R30: data links expire after 24 hours — sign them again for every attempt.
                if (sw.Env.TryGetValue("MLP_JOB_SPEC", out var jobSpec))
                {
                    sw.Env["MLP_JOB_SPEC"] = await sr.ServiceProvider.GetRequiredService<IMediator>().Send(
                        new MLPlatform.ControlPlane.Application.Tracking.RefreshRunDataUrlsCommand(a.WorkspaceId, rid, jobSpec), ct);
                    // Host dataset cache: the dataset's volume stays on the machine across runs; the shim downloads only missing files into it.
                    if (System.Text.Json.Nodes.JsonNode.Parse(sw.Env["MLP_JOB_SPEC"]) is System.Text.Json.Nodes.JsonObject js
                        && Guid.TryParse(js["datasetId"]?.ToString(), out var dsId) && js["dataCache"]?.ToString() is { Length: > 0 } cacheMount)
                    {
                        sw.Mounts.Add(new Mount { Source = MLPlatform.ControlPlane.Application.Data.DataCacheRules.Volume(dsId), Target = cacheMount });
                        sw.Labels["mlp.datacache"] = dsId.ToString("N");
                    }
                }
            }
            // Image build and checkpoint lookup can take minutes: a job cancelled meanwhile must not start its container.
            await using (var sj = scopes.CreateAsyncScope())
                if (await sj.ServiceProvider.GetRequiredService<IMediator>().Query(new JobStateQuery(a.JobId), ct) == "cancelled")
                {
                    await ray.StopAsync(a.Id.ToString(), ct);
                    return;
                }
            var res = await session.SendCommandAsync(new AgentCommand { ContainerApply = new ContainerApply { Spec = sw } }, TimeSpan.FromMinutes(30), ct);
            if (!res.Ok)
            {
                await FailAsync(a.Id, res.ErrorReason, res.Message, ct);
                return;
            }
            if (isNotebook)
            {
                await using var sn = scopes.CreateAsyncScope();
                await sn.ServiceProvider.GetRequiredService<IMediator>().Send(
                    new MLPlatform.ControlPlane.Application.Notebooks.SetNotebookStateByJobCommand(a.JobId, "running", null, alloc.HostId, res.Endpoint), ct);
            }
            if (isServe)
            {
                var endpoint = string.IsNullOrEmpty(res.Endpoint) ? $"{alloc.NodeIp}:{servePort}" : res.Endpoint;
                await using var s1 = scopes.CreateAsyncScope();
                await s1.ServiceProvider.GetRequiredService<IMediator>().Send(
                    new MLPlatform.ControlPlane.Application.Serving.SetDeploymentStateByJobCommand(a.JobId, "deploying", "Loading model", endpoint), ct);
            }
        }
        await using var scope = scopes.CreateAsyncScope();
        if (!await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new MarkAttemptRunningCommand(a.Id, a.JobId), ct))
        {
            // Cancelled while the container was starting: stop it and free the reservation.
            foreach (var alloc in done.Allocations)
                if (agents.TryGet(alloc.HostId, out var s2))
                    await s2.SendCommandAsync(new AgentCommand { ContainerStop = new ContainerStop { Label = "mlp.attempt=" + a.Id, GraceSeconds = 10 } }, TimeSpan.FromMinutes(2), ct);
            await ray.StopAsync(a.Id.ToString(), ct);
            return;
        }
        log.LogInformation("Attempt {Attempt} running on {N} host(s)", a.Id, done.Allocations.Count);
    }

    /// <summary>Cancel the Ray holder job of an attempt that has not secured resources yet.</summary>
    public Task AbandonReservationAsync(Guid attemptId, CancellationToken ct) => ray.StopAsync(attemptId.ToString(), ct);

    /// <summary>
    /// Build the recipe image on the host (first use) and return its tag; null after reporting a failure. Every step is an attempt event, so the
    /// job page shows what the machine is doing (a first D-FINE build downloads ~5 GB and can take 10–30 minutes).
    /// </summary>
    private async Task<string?> PrepareRecipeImageAsync(MLPlatform.ControlPlane.Application.Scheduling.AttemptRow a, Guid hostId, AgentSession session, string image, bool isServe, CancellationToken ct)
    {
        async Task Event(string kind, string level, string msg)
        {
            await using var sb = scopes.CreateAsyncScope();
            await sb.ServiceProvider.GetRequiredService<IMediator>().Send(new MLPlatform.ControlPlane.Application.Scheduling.EmitAttemptEventCommand(a.WorkspaceId, a.Id, kind, level, msg), ct);
        }
        var arch = RecipeEnvironments.Arch(session.Info);
        await Event("attempt.image_build", "info", $"Preparing mlp-shim for linux-{arch} (built once by agent-builder)");
        var shim = await envs.ShimAsync(arch, ct);
        if (shim is null)
        {
            await Event("attempt.image_failed", "error", "mlp-shim is not available (agent-builder missing or its build failed)");
            await FailAsync(a.Id, "image_build_failed", "mlp-shim build is not available", ct);
            return null;
        }
        var rbuild = envs.Build(image, shim)!;
        if (isServe)
        {
            await using var sb = scopes.CreateAsyncScope();
            await sb.ServiceProvider.GetRequiredService<IMediator>().Send(new MLPlatform.ControlPlane.Application.Serving.SetDeploymentStateByJobCommand(
                a.JobId, "deploying", $"Preparing image {rbuild.Tag} on the host (the first build can take 10–30 minutes)", null), ct);
        }
        await Event("attempt.image_build", "info", $"Building image {rbuild.Tag} on {hostId} (limit {rbuild.TimeoutSeconds / 60} min; reused once built)");
        var started = TimeProvider.System.GetTimestamp();
        var br = await session.SendCommandAsync(new AgentCommand { ImageBuild = rbuild }, TimeSpan.FromMinutes(65), ct);
        var took = TimeProvider.System.GetElapsedTime(started);
        await Event(br.Ok ? "attempt.image_ready" : "attempt.image_failed", br.Ok ? "info" : "error",
            br.Ok ? (br.Kept ? $"Image {rbuild.Tag} already on the machine" : $"Image {rbuild.Tag} built in {took.TotalMinutes:0.0} min")
                  : $"Image build failed after {took.TotalMinutes:0.0} min: {br.ErrorReason} {br.Message}");
        // The canvas reads this to know which machine has the environment (round 1 slice 2).
        await envs.RecordAsync(a.WorkspaceId, image, rbuild.Tag, hostId, br.Ok, $"{br.ErrorReason} {br.Message}".Trim());
        if (!br.Ok) { await FailAsync(a.Id, br.ErrorReason, "Recipe image build failed: " + br.Message, ct); return null; }
        return rbuild.Tag;
    }

    public async Task FailAsync(Guid attemptId, string reason, string message, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        var r = await m.Send(new FinishAttemptCommand(attemptId, 1, reason, message), ct);
        // Launch failed: the notebook / deployment must see it right away (previously it stayed at "queued" forever).
        var text = string.IsNullOrEmpty(message) ? reason : message;
        await m.Send(new MLPlatform.ControlPlane.Application.Notebooks.SetNotebookStateByJobCommand(r.JobId,
            r.JobState == "queued" ? "starting" : "failed", r.JobState == "queued" ? "Retrying: " + text : text, null, null), ct);
        await m.Send(new MLPlatform.ControlPlane.Application.Serving.SetDeploymentStateByJobCommand(r.JobId,
            r.JobState == "queued" ? "deploying" : "failed", text, null), ct);
        await m.Send(new MLPlatform.ControlPlane.Application.Tracking.SyncRunStateCommand(r.JobId, r.JobState), ct);
        await FinishTrialAsync(m, r.JobId, attemptId, r.JobState, text, ct);
        await ray.StopAsync(attemptId.ToString(), ct);
    }

    /// <summary>A trial's hidden run finished: hand its "[mlp-trial]" lines to the trial (a job that is not a trial is ignored by the command).</summary>
    private async Task FinishTrialAsync(IMediator m, Guid jobId, Guid attemptId, string jobState, string? headline, CancellationToken ct)
    {
        if (jobState is "queued" or "running" or "starting") return;
        var owner = await m.Query(new MLPlatform.ControlPlane.Application.Ops.AttemptOwnerQuery(attemptId), ct);
        var lines = owner is null ? [] : await logs.TrialLinesAsync(owner.WorkspaceId, owner.JobId, attemptId, ct);
        await m.Send(new MLPlatform.ControlPlane.Application.Tracking.FinishTrialCommand(jobId, attemptId, jobState, lines,
            string.IsNullOrEmpty(headline) ? null : headline), ct);
    }

    /// <summary>Stop a running job (cancel, stop deployment): StopWorkload on every host of the attempt + stop the Ray job to free resources.</summary>
    public async Task StopJobAsync(Guid jobId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        // Attempts are read before the cancel: the cancel closes the ones without a container, and their Ray holder must still be stopped.
        var attempts = (await m.Query(new ListActiveAttemptsQuery(), ct)).Where(x => x.JobId == jobId).ToList();
        await m.Send(new CancelJobCommand(jobId, "Stopped on request"), ct);
        foreach (var a in attempts)
        {
            foreach (var h in a.HostIds)
                if (agents.TryGet(h, out var session))
                    await session.SendCommandAsync(new AgentCommand { ContainerStop = new ContainerStop { Label = "mlp.attempt=" + a.Id, GraceSeconds = 30 } },
                        TimeSpan.FromMinutes(2), ct);
            await ray.StopAsync(a.Id.ToString(), ct);
        }
    }

    /// <summary>Stop an attempt's containers (without cancelling the job): used when replacing an attempt (signal lost, host change).</summary>
    public async Task StopAttemptAsync(Guid attemptId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        foreach (var a in (await scope.ServiceProvider.GetRequiredService<IMediator>().Query(new ListActiveAttemptsQuery(), ct)).Where(x => x.Id == attemptId))
            foreach (var h in a.HostIds)
                if (agents.TryGet(h, out var session))
                    await session.SendCommandAsync(new AgentCommand { ContainerStop = new ContainerStop { Label = "mlp.attempt=" + a.Id, GraceSeconds = 30 } }, TimeSpan.FromMinutes(2), ct);
    }

    /// <summary>Container finished on a host → finalize the attempt, stop the Ray job to free the placement group.</summary>
    /// <summary>Finalize the attempt; returns the job state afterwards (null if not a workload).</summary>
    public async Task<string?> OnExitAsync(ContainerState c, CancellationToken ct)
    {
        if (!c.Labels.TryGetValue("mlp.attempt", out var attemptId) || !Guid.TryParse(attemptId, out var id)) return null;
        // Say why, not just "exited with code 1": out of memory, or the error the program printed last (the agent sends the full log before the exit).
        var why = c.OomKilled ? "The container ran out of memory and was stopped — lower the batch size or the number of data workers"
            : c.ExitCode != 0 ? ErrorHeadline.From(logs.Tail(id, 0, 600).Select(l => l.Text).ToList()) ?? "" : "";
        // R26: the shim ends a recipe-declared failure with "MLP_FAIL {reason, message}" — that reason and message replace the generic headline.
        string failReason = "";
        if (!c.OomKilled && c.ExitCode == 2 && logs.Tail(id, 0, 200).Select(l => l.Text).LastOrDefault(t => t.StartsWith("MLP_FAIL ", StringComparison.Ordinal)) is { } fl)
            try
            {
                if (System.Text.Json.Nodes.JsonNode.Parse(fl[9..]) is System.Text.Json.Nodes.JsonObject fo)
                {
                    failReason = fo["reason"]?.ToString() is { } fr && System.Text.RegularExpressions.Regex.IsMatch(fr, "^[a-z][a-z0-9_]{0,40}$") ? fr : "";
                    if (fo["message"]?.ToString() is { Length: > 0 } fm) why = fm;
                }
            }
            catch (System.Text.Json.JsonException) { }
        var exit = new { AttemptId = attemptId, ExitCode = c.OomKilled ? 137 : c.ExitCode, Reason = c.OomKilled ? "oom" : failReason, Message = why };
        await logs.FlushAsync(id);   // the attempt is over: its whole log goes to storage now
        await using var scope = scopes.CreateAsyncScope();
        var r = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
            new FinishAttemptCommand(id, exit.ExitCode, string.IsNullOrEmpty(exit.Reason) ? null : exit.Reason, exit.Message), ct);
        if (r.Stale)
        {
            // An old container of a replaced attempt went away (fenced, or reported late): the current attempt owns the job's state.
            log.LogInformation("Attempt {Attempt} exited {Code} after it was replaced — ignored", id, exit.ExitCode);
            return r.JobState;
        }
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
            new MLPlatform.ControlPlane.Application.Tracking.SyncRunStateCommand(r.JobId, r.JobState), ct);
        await FinishTrialAsync(scope.ServiceProvider.GetRequiredService<IMediator>(), r.JobId, id, r.JobState, why, ct);
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
            new MLPlatform.ControlPlane.Application.Serving.SetDeploymentStateByJobCommand(r.JobId,
                r.JobState == "queued" ? "deploying" : "failed", $"Replica exited with code {exit.ExitCode}{(why.Length > 0 ? " — " + why : "")}", null), ct);
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
            new MLPlatform.ControlPlane.Application.Notebooks.SetNotebookStateByJobCommand(r.JobId,
                r.JobState == "queued" ? "starting" : exit.ExitCode == 0 ? "stopped" : "failed", $"Container exited with code {exit.ExitCode}{(why.Length > 0 ? " — " + why : "")}", null, null), ct);
        await ray.StopAsync(exit.AttemptId, ct);
        log.LogInformation("Attempt {Attempt} exited {Code} → job {State}", id, exit.ExitCode, r.JobState);
        return r.JobState;
    }
}

/// <summary>
/// D-36 (20/10 §4.4): a `suspect` attempt past its wait for the host to return → `node_lost` → the scheduler retries (same or another host) from the checkpoint.
/// </summary>
public sealed class RecoveryLoop(IServiceScopeFactory scopes, WorkloadLauncher launcher, SystemHealth health, ILogger<RecoveryLoop> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stop))
        {
            try
            {
                health.Beat("recovery", 5);
                await using var scope = scopes.CreateAsyncScope();
                var med = scope.ServiceProvider.GetRequiredService<IMediator>();
                foreach (var s in await med.Query(new MLPlatform.ControlPlane.Application.Ops.ListExpiredSuspectsQuery(), stop))
                {
                    log.LogWarning("Attempt {Attempt} timed out waiting for host → node_lost", s.Id);
                    await launcher.FailAsync(s.Id, "node_lost", "The host did not come back within the wait period", stop);
                }
                foreach (var id in await med.Send(new MLPlatform.ControlPlane.Application.Scheduling.ExpireReservationsCommand(TimeSpan.FromMinutes(3)), stop))
                {
                    log.LogWarning("Attempt {Attempt} held resources for over 10 minutes → cancelling holder, requeueing", id);
                    await launcher.AbandonReservationAsync(id, stop);
                }
                // 20/10 §2.3: shim signal lost for too long (host still online) → stop the container and retry from the checkpoint.
                foreach (var id in await med.Send(new MLPlatform.ControlPlane.Application.Ops.DetectAnomaliesCommand(), stop))
                {
                    log.LogWarning("Attempt {Attempt} lost signal for too long → stopping and retrying", id);
                    await launcher.StopAttemptAsync(id, stop);
                    await launcher.FailAsync(id, "signal_lost", "The training process has been silent too long; stopping and retrying from the checkpoint", stop);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Recovery loop failed"); }
        }
    }
}

/// <summary>
/// Replica probing (M12 R2/R5): every 5 s call ModelServer.Health; success → healthy, failure → degraded (the gateway drops it from routing).
/// </summary>
public sealed class DeploymentProber(IServiceScopeFactory scopes, ModelServerClients clients, SystemHealth health, WorkloadLauncher launcher,
    ILogger<DeploymentProber> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stop))
        {
            health.Beat("deployment-prober", 5);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var m = scope.ServiceProvider.GetRequiredService<IMediator>();
                // Playground test replicas that nobody called for idle_stop_min minutes: free the GPU.
                foreach (var d in await m.Query(new MLPlatform.ControlPlane.Application.Serving.ListIdleTestDeploymentsQuery(), stop))
                {
                    await m.Send(new MLPlatform.ControlPlane.Application.Serving.StopIdleDeploymentCommand(d.Id, d.IdleStopMin ?? 0), stop);
                    if (d.JobId is { } job) await launcher.StopJobAsync(job, stop);
                    log.LogInformation("Test replica {Slug} stopped after {Min} idle minutes", d.Slug, d.IdleStopMin);
                }
                foreach (var d in await m.Query(new MLPlatform.ControlPlane.Application.Serving.ListProbeTargetsQuery(), stop))
                {
                    var ok = await clients.HealthAsync(d.Endpoint!, stop);
                    var next = ok ? "healthy" : d.State == "healthy" ? "degraded" : d.State;
                    if (next != d.State)
                    {
                        await m.Send(new MLPlatform.ControlPlane.Application.Serving.SetDeploymentHealthCommand(d.Id, next, ok ? null : "Replica is not responding"), stop);
                        log.LogInformation("Deployment {Slug}: {From} → {To}", d.Slug, d.State, next);
                    }
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Deployment probe failed");
            }
        }
    }
}

/// <summary>gRPC channel to a replica, reused per endpoint.</summary>
public sealed class ModelServerClients
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Grpc.Net.Client.GrpcChannel> _channels = new();

    public ModelServer.ModelServerClient For(string endpoint) =>
        new(_channels.GetOrAdd(endpoint, e => Grpc.Net.Client.GrpcChannel.ForAddress("http://" + e)));

    public async Task<bool> HealthAsync(string endpoint, CancellationToken ct)
    {
        try
        {
            var r = await For(endpoint).HealthAsync(new HealthRequest(), deadline: TimeProvider.System.GetUtcNow().UtcDateTime.AddSeconds(3), cancellationToken: ct);
            return r.Ready;
        }
        catch (Grpc.Core.RpcException) { return false; }
    }
}
