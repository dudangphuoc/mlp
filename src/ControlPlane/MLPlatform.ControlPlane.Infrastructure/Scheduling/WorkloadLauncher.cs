using System.Text.Json;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Ray;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>Fully allocated → send StartWorkload to the agent on each host (20/08 §9 step 3).</summary>
public sealed class WorkloadLauncher(AgentRegistry agents, IServiceScopeFactory scopes, RayJobsClient ray, IConfiguration cfg, RecipeEnvironments envs, LogStore logs, ILogger<WorkloadLauncher> log) : MLPlatform.ControlPlane.Application.Scheduling.IWorkloadControl
{
    private const int ServePortBase = 8500;
    private const int ServePortRange = 100;

    public async Task LaunchAsync(AllocationComplete done, CancellationToken ct)
    {
        var (workloadToken, runId) = await RunContextAsync(done.Attempt, ct);
        var rank = 0;
        foreach (var alloc in done.Allocations)
        {
            if (!agents.TryGet(alloc.HostId, out var session))
            {
                await FailAsync(done.Attempt.Id, "node_lost", $"Host {alloc.HostId} was offline at launch", ct);
                return;
            }
            if (!await LaunchOnHostAsync(done, alloc, session, rank++, workloadToken, runId, ct)) return;
        }
        await MarkRunningOrStopAsync(done, ct);
    }

    private async Task<(string WorkloadToken, Guid? RunId)> RunContextAsync(AttemptRow attempt, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var token = await mediator.Send(new MLPlatform.ControlPlane.Application.Tracking.MintWorkloadTokenCommand(attempt.Id), ct);
        var runId = await mediator.Query(new MLPlatform.ControlPlane.Application.Tracking.GetRunIdForJobQuery(attempt.JobId), ct);
        return (token, runId);
    }

    private async Task<bool> LaunchOnHostAsync(AllocationComplete done, AllocationRow alloc, AgentSession session, int nodeRank, string workloadToken, Guid? runId, CancellationToken ct)
    {
        var a = done.Attempt;
        var spec = done.Spec;
        var isServe = spec.Env.ContainsKey("MLP_DEPLOYMENT");
        var isNotebook = spec.Env.ContainsKey("MLP_NOTEBOOK");
        var sw = BaseSpec(a, spec, alloc, nodeRank);
        var servePort = isServe ? ConfigureServing(sw, alloc) : 0;
        if (!isNotebook && HostContainers.RecipeImage(spec.Image, cfg["Catalog:RecipesDir"]) is not null)
        {
            if (await PrepareRecipeImageAsync(a, alloc.HostId, session, spec.Image, isServe, ct) is not { } tag) return false;
            sw.Image = tag;
        }
        if (spec.Env.TryGetValue("MLP_SHM_GB", out var shmGb) && ulong.TryParse(shmGb, out var gb) && gb > 0) sw.ShmBytes = gb << 30;
        if (isNotebook && !await ConfigureNotebookAsync(sw, a, spec, alloc, session, ct)) return false;
        sw.Command.AddRange(spec.Command);
        foreach (var (k, v) in spec.Env) sw.Env[k] = v;
        if (isServe) await AddModelFilesAsync(sw, a, spec, ct);
        AddDataCaches(sw, spec);
        AddAttemptEnv(sw, done, nodeRank, workloadToken);
        if (runId is { } rid) await AddRunOutputAsync(sw, a, alloc.HostId, rid, ct);
        if (await IsCancelledAsync(a.JobId, ct))
        {
            await ray.StopAsync(a.Id.ToString(), ct);
            return false;
        }
        var res = await session.SendCommandAsync(new AgentCommand { ContainerApply = new ContainerApply { Spec = sw } }, TimeSpan.FromMinutes(30), ct);
        if (!res.Ok)
        {
            if (res.ErrorReason == "FLEET_GPU_YIELDED") await YieldAsync(done, nodeRank, res.Message, ct);
            else await FailAsync(a.Id, res.ErrorReason, res.Message, ct);
            return false;
        }
        await ReportStartedAsync(a, alloc, res.Endpoint, isNotebook, isServe, servePort, ct);
        return true;
    }

    private ContainerSpec BaseSpec(AttemptRow a, JobSpecDto spec, AllocationRow alloc, int nodeRank)
    {
        var sw = new ContainerSpec
        {
            Name = HostContainers.WorkloadName(a.Id.ToString(), nodeRank), Image = spec.Image, Cpus = spec.Cpus, MemoryBytes = (ulong)spec.MemoryBytes, Init = true,
            Network = !string.IsNullOrEmpty(spec.Network) ? spec.Network : Overlay.HubPublicKey(cfg) is not null ? Overlay.ContainerNetwork : cfg["Scheduler:DefaultWorkloadNetwork"] ?? "",
        };
        sw.Labels["mlp.role"] = "workload";
        sw.Labels["mlp.attempt"] = a.Id.ToString();
        sw.Labels["mlp.logs"] = a.Id.ToString();
        sw.Gpus.AddRange(alloc.GpuUuids);
        return sw;
    }

    private int ConfigureServing(ContainerSpec sw, AllocationRow alloc)
    {
        var port = ServePortBase + Random.Shared.Next(ServePortRange);
        if (string.Equals(cfg["Serving:PublishToHost"], "true", StringComparison.OrdinalIgnoreCase))
            sw.Ports.Add(new PortBinding { HostIp = "127.0.0.1", ContainerPort = port });
        sw.Env["MLP_SERVE_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        sw.Env["MLP_DEVICE"] = alloc.GpuUuids.Length > 0 ? "cuda" : "cpu";
        return port;
    }

    private async Task<bool> ConfigureNotebookAsync(ContainerSpec sw, AttemptRow a, JobSpecDto spec, AllocationRow alloc, AgentSession session, CancellationToken ct)
    {
        sw.Ports.Add(new PortBinding { HostIp = "127.0.0.1", ContainerPort = MLPlatform.ControlPlane.Application.Notebooks.NotebookRules.ContainerPort });
        sw.Mounts.Add(new Mount { Source = spec.Env["MLP_HOME_VOLUME"], Target = MLPlatform.ControlPlane.Application.Notebooks.NotebookRules.HomeMount });
        if (string.IsNullOrEmpty(spec.Network)) sw.Network = cfg["Scheduler:PublishNetwork"] ?? "";
        if (HostContainers.NotebookImage(spec.Image) is not { } build) return true;
        await using (var scope = scopes.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new MLPlatform.ControlPlane.Application.Notebooks.SetNotebookStateByJobCommand(
                a.JobId, "starting", "Preparing the notebook image on the host (the first time may take a few minutes)", alloc.HostId, null), ct);
        var built = await session.SendCommandAsync(new AgentCommand { ImageBuild = build }, TimeSpan.FromMinutes(45), ct);
        if (built.Ok) return true;
        await FailAsync(a.Id, built.ErrorReason, "Notebook image build failed: " + built.Message, ct);
        return false;
    }

    private async Task AddModelFilesAsync(ContainerSpec sw, AttemptRow a, JobSpecDto spec, CancellationToken ct)
    {
        if (!spec.Env.TryGetValue("MLP_MODEL_VERSION", out var mv) || !Guid.TryParse(mv, out var modelVersion)) return;
        await using var scope = scopes.CreateAsyncScope();
        sw.Env["MLP_MODEL_FILES"] = await scope.ServiceProvider.GetRequiredService<IMediator>().Query(
            new MLPlatform.ControlPlane.Application.Serving.ResolveModelFilesQuery(a.WorkspaceId, modelVersion), ct);
        sw.Mounts.Add(new Mount { Source = $"mlp-model-{modelVersion:N}", Target = "/mnt/model" });
    }

    private static void AddDataCaches(ContainerSpec sw, JobSpecDto spec)
    {
        if (!spec.Env.TryGetValue("MLP_DATA_CACHES", out var caches)) return;
        foreach (var c in caches.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (Guid.TryParse(c, out var datasetId))
                sw.Mounts.Add(new Mount { Source = MLPlatform.ControlPlane.Application.Data.DataCacheRules.Volume(datasetId), Target = MLPlatform.ControlPlane.Application.Data.DataCacheRules.Mount(datasetId) });
    }

    private void AddAttemptEnv(ContainerSpec sw, AllocationComplete done, int nodeRank, string workloadToken)
    {
        sw.Env["MLP_ATTEMPT_ID"] = done.Attempt.Id.ToString();
        sw.Env["MLP_NODE_RANK"] = nodeRank.ToString(System.Globalization.CultureInfo.InvariantCulture);
        sw.Env["MLP_NNODES"] = done.Allocations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        sw.Env["MLP_MASTER_ADDR"] = done.Allocations[0].NodeIp;
        sw.Env["MLP_JOB_TOKEN"] = workloadToken;
        using (var act = MLPlatform.Cqrs.Pipeline.Source.StartActivity("LaunchAttempt")) if (act?.Id is { } tp) sw.Env["TRACEPARENT"] = tp;
        sw.Env["MLP_API"] = cfg["Scheduler:ApiAddressForWorkloads"] ?? cfg["Scheduler:ApiAddressForRay"] ?? "10.88.0.1:5080";
    }

    private async Task AddRunOutputAsync(ContainerSpec sw, AttemptRow a, Guid hostId, Guid runId, CancellationToken ct)
    {
        sw.Env["MLP_RUN_ID"] = runId.ToString();
        sw.Mounts.Add(new Mount { Source = $"mlp-run-{runId:N}", Target = "/mnt/output" });
        sw.Labels["mlp.volume"] = $"mlp-run-{runId:N}";
        await using var scope = scopes.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        if (await mediator.Send(new MLPlatform.ControlPlane.Application.Ops.ResolveResumeCommand(a.Id, a.JobId, hostId), ct) is { } resume)
            sw.Env["MLP_RESUME_FROM"] = resume;
        if (!sw.Env.TryGetValue("MLP_JOB_SPEC", out var jobSpec)) return;
        sw.Env["MLP_JOB_SPEC"] = await mediator.Send(new MLPlatform.ControlPlane.Application.Tracking.RefreshRunDataUrlsCommand(a.WorkspaceId, runId, jobSpec), ct);
        if (System.Text.Json.Nodes.JsonNode.Parse(sw.Env["MLP_JOB_SPEC"]) is System.Text.Json.Nodes.JsonObject js
            && Guid.TryParse(js["datasetId"]?.ToString(), out var datasetId) && js["dataCache"]?.ToString() is { Length: > 0 } cacheMount)
        {
            sw.Mounts.Add(new Mount { Source = MLPlatform.ControlPlane.Application.Data.DataCacheRules.Volume(datasetId), Target = cacheMount });
            sw.Labels["mlp.datacache"] = datasetId.ToString("N");
        }
    }

    private async Task<bool> IsCancelledAsync(Guid jobId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Query(new JobStateQuery(jobId), ct) == "cancelled";
    }

    private async Task ReportStartedAsync(AttemptRow a, AllocationRow alloc, string endpoint, bool isNotebook, bool isServe, int servePort, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        if (isNotebook)
            await mediator.Send(new MLPlatform.ControlPlane.Application.Notebooks.SetNotebookStateByJobCommand(a.JobId, "running", null, alloc.HostId, endpoint), ct);
        if (isServe)
            await mediator.Send(new MLPlatform.ControlPlane.Application.Serving.SetDeploymentStateByJobCommand(a.JobId, "deploying", "Loading model",
                string.IsNullOrEmpty(endpoint) ? $"{alloc.NodeIp}:{servePort}" : endpoint), ct);
    }

    private async Task MarkRunningOrStopAsync(AllocationComplete done, CancellationToken ct)
    {
        var a = done.Attempt;
        await using var scope = scopes.CreateAsyncScope();
        if (await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new MarkAttemptRunningCommand(a.Id, a.JobId), ct))
        {
            log.LogInformation("Attempt {Attempt} running on {N} host(s)", a.Id, done.Allocations.Count);
            return;
        }
        foreach (var alloc in done.Allocations)
            if (agents.TryGet(alloc.HostId, out var session))
                await session.SendCommandAsync(new AgentCommand { ContainerStop = new ContainerStop { Label = "mlp.attempt=" + a.Id, GraceSeconds = 10 } }, TimeSpan.FromMinutes(2), ct);
        await ray.StopAsync(a.Id.ToString(), ct);
    }

    /// <summary>Cancel the Ray holder job of an attempt that has not secured resources yet.</summary>
    public Task AbandonReservationAsync(Guid attemptId, CancellationToken ct) => ray.StopAsync(attemptId.ToString(), ct);

    /// <summary>Build the recipe image on the host (first use) and return its tag; null after reporting a
    /// failure.</summary>
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
        await envs.RecordAsync(a.WorkspaceId, image, rbuild.Tag, hostId, br.Ok, $"{br.ErrorReason} {br.Message}".Trim());
        if (!br.Ok) { await FailAsync(a.Id, br.ErrorReason, "Recipe image build failed: " + br.Message, ct); return null; }
        return rbuild.Tag;
    }

    /// <summary>The agent yielded its GPU to another environment: remove the ranks already started, release the
    /// placement group and put the job back in the queue without counting the attempt.</summary>
    private async Task YieldAsync(AllocationComplete done, int failedRank, string holders, CancellationToken ct)
    {
        var a = done.Attempt;
        foreach (var alloc in done.Allocations.Take(failedRank))
            if (agents.TryGet(alloc.HostId, out var s))
                await s.SendCommandAsync(new AgentCommand { ContainerRemove = new ContainerRemove { Label = "mlp.attempt=" + a.Id, Force = true } }, TimeSpan.FromMinutes(2), ct);
        await ray.StopAsync(a.Id.ToString(), ct);
        await using var scope = scopes.CreateAsyncScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        var jobId = await m.Send(new MLPlatform.ControlPlane.Application.Scheduling.YieldAttemptCommand(a.Id, holders), ct);
        await m.Send(new MLPlatform.ControlPlane.Application.Notebooks.SetNotebookStateByJobCommand(jobId, "starting",
            MLPlatform.ControlPlane.Application.Scheduling.YieldAttemptCommandHandler.Reason, null, null), ct);
        await m.Send(new MLPlatform.ControlPlane.Application.Serving.SetDeploymentStateByJobCommand(jobId, "deploying",
            MLPlatform.ControlPlane.Application.Scheduling.YieldAttemptCommandHandler.Reason, null), ct);
        log.LogInformation("Attempt {Attempt}: GPU yielded to {Holders}; job {Job} waits", a.Id, holders, jobId);
    }

    public async Task FailAsync(Guid attemptId, string reason, string message, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
        var r = await m.Send(new FinishAttemptCommand(attemptId, 1, reason, message), ct);
        var text = string.IsNullOrEmpty(message) ? reason : message;
        await m.Send(new MLPlatform.ControlPlane.Application.Notebooks.SetNotebookStateByJobCommand(r.JobId,
            r.JobState == "queued" ? "starting" : "failed", r.JobState == "queued" ? "Retrying: " + text : text, null, null), ct);
        await m.Send(new MLPlatform.ControlPlane.Application.Serving.SetDeploymentStateByJobCommand(r.JobId,
            r.JobState == "queued" ? "deploying" : "failed", text, null), ct);
        await m.Send(new MLPlatform.ControlPlane.Application.Tracking.SyncRunStateCommand(r.JobId, r.JobState), ct);
        await FinishTrialAsync(m, r.JobId, attemptId, r.JobState, text, ct);
        await ray.StopAsync(attemptId.ToString(), ct);
    }

    /// <summary>A trial's hidden run finished: hand its "[mlp-trial]" lines to the trial (a job that is not a trial is
    /// ignored by the command).</summary>
    private async Task FinishTrialAsync(IMediator m, Guid jobId, Guid attemptId, string jobState, string? headline, CancellationToken ct)
    {
        if (jobState is "queued" or "running" or "starting") return;
        var owner = await m.Query(new MLPlatform.ControlPlane.Application.Ops.AttemptOwnerQuery(attemptId), ct);
        var lines = owner is null ? [] : await logs.TrialLinesAsync(owner.WorkspaceId, owner.JobId, attemptId, ct);
        await m.Send(new MLPlatform.ControlPlane.Application.Tracking.FinishTrialCommand(jobId, attemptId, jobState, lines,
            string.IsNullOrEmpty(headline) ? null : headline), ct);
    }

    /// <summary>Stop a running job (cancel, stop deployment): StopWorkload on every host of the attempt + stop the Ray
    /// job to free resources.</summary>
    public async Task StopJobAsync(Guid jobId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var m = scope.ServiceProvider.GetRequiredService<IMediator>();
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

    /// <summary>Stop an attempt's containers (without cancelling the job): used when replacing an attempt (signal lost,
    /// host change).</summary>
    public async Task StopAttemptAsync(Guid attemptId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        foreach (var a in (await scope.ServiceProvider.GetRequiredService<IMediator>().Query(new ListActiveAttemptsQuery(), ct)).Where(x => x.Id == attemptId))
            foreach (var h in a.HostIds)
                if (agents.TryGet(h, out var session))
                    await session.SendCommandAsync(new AgentCommand { ContainerStop = new ContainerStop { Label = "mlp.attempt=" + a.Id, GraceSeconds = 30 } }, TimeSpan.FromMinutes(2), ct);
    }

    /// <summary>Container finished on a host → finalize the attempt, stop the Ray job to free the placement
    /// group.</summary>
    public async Task<string?> OnExitAsync(ContainerState c, CancellationToken ct)
    {
        if (!c.Labels.TryGetValue("mlp.attempt", out var attemptId) || !Guid.TryParse(attemptId, out var id)) return null;
        var why = c.OomKilled ? "The container ran out of memory and was stopped — lower the batch size or the number of data workers"
            : c.ExitCode != 0 ? ErrorHeadline.From(logs.Tail(id, 0, 600).Select(l => l.Text).ToList()) ?? "" : "";
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
        await logs.FlushAsync(id);
        await using var scope = scopes.CreateAsyncScope();
        var r = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
            new FinishAttemptCommand(id, exit.ExitCode, string.IsNullOrEmpty(exit.Reason) ? null : exit.Reason, exit.Message), ct);
        if (r.Stale)
        {
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
