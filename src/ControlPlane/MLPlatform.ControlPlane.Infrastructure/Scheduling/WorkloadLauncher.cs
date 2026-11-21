using System.Text.Json;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using MLPlatform.Ray;

namespace MLPlatform.ControlPlane.Infrastructure;

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
