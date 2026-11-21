using MLPlatform.ControlPlane.Infrastructure;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Empty = MLPlatform.Contracts.V1.Empty;

namespace MLPlatform.Api.Grpc;

public sealed class TrackingGrpc(IMediator mediator, MLPlatform.ControlPlane.Infrastructure.WorkloadLauncher launcher, MLPlatform.Storage.S3Client s3,
    MLPlatform.ControlPlane.Infrastructure.LogStore logs) : TrackingService.TrackingServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    public override async Task<Project> CreateProject(CreateProjectRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreateProjectCommand(G(r.WorkspaceId, "workspace_id"), r.Slug, r.Name, r.Task), ctx.CancellationToken));

    public override async Task<ListProjectsResponse> ListProjects(ListProjectsRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        var stats = (await mediator.Query(new ListProjectStatsQuery(ws), ctx.CancellationToken)).ToDictionary(s => s.ProjectId);
        var res = new ListProjectsResponse();
        foreach (var row in await mediator.Query(new ListProjectsQuery(ws), ctx.CancellationToken))
        {
            var p = ToProto(row);
            if (stats.TryGetValue(row.Id, out var s))
            {
                p.RunCount = (int)s.RunCount; p.ActiveCount = (int)s.ActiveCount;
                if (s.LastRunAt is { } last) p.LastRunAt = Timestamp.FromDateTimeOffset(last);
                if (s.BestMetricValue is { } best)
                {
                    p.HasBest = true; p.BestMetricValue = best; p.BestMetricKey = s.BestMetricKey ?? ""; p.BestRunNumber = s.BestNumber ?? 0; p.BestRunName = s.BestName ?? "";
                }
            }
            res.Projects.Add(p);
        }
        return res;
    }

    public override async Task<Run> StartTraining(StartTrainingRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        if (!string.IsNullOrEmpty(r.ProjectRecipeId))
            return await View(ws, (await mediator.Send(new StartDraftTrainingCommand(ws, G(r.ProjectRecipeId, "project_recipe_id")), ctx.CancellationToken)).Id, ctx);
        var run = await mediator.Send(new StartTrainingCommand(ws, G(r.ProjectId, "project_id"), r.Name, r.Recipe,
            r.Variant, r.Task, r.ParamsJson, r.FromScratch, r.Cpus, r.Gpus,
            string.IsNullOrEmpty(r.ImageOverride) ? null : r.ImageOverride, string.IsNullOrEmpty(r.Network) ? null : r.Network,
            string.IsNullOrEmpty(r.DatasetVersionId) ? null : G(r.DatasetVersionId, "dataset_version_id")), ctx.CancellationToken);
        return await View(ws, run.Id, ctx);
    }

    public override async Task<ListRunsResponse> ListRuns(ListRunsRequest r, ServerCallContext ctx)
    {
        var res = new ListRunsResponse();
        res.Runs.AddRange((await mediator.Query(new ListRunViewsQuery(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override Task<Run> GetRun(GetRunRequest r, ServerCallContext ctx) => View(G(r.WorkspaceId, "workspace_id"), G(r.RunId, "run_id"), ctx);

    private async Task<Run> View(Guid ws, Guid runId, ServerCallContext ctx) =>
        ToProto(await mediator.Query(new GetRunViewQuery(ws, runId), ctx.CancellationToken));

    public override async Task<Empty> LogMetrics(LogMetricsRequest r, ServerCallContext ctx)
    {
        var auth = ctx.RequestHeaders.GetValue("authorization") ?? "";
        var workload = auth.StartsWith("Bearer mlpj_", StringComparison.Ordinal) ? auth[7..] : null;
        await mediator.Send(new LogMetricsCommand(G(r.RunId, "run_id"), workload,
            r.Points.Select(p => (p.Key, p.Step, p.Value, p.At is null ? (DateTimeOffset?)null : p.At.ToDateTimeOffset())).ToList()), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<GetMetricsResponse> GetMetrics(GetMetricsRequest r, ServerCallContext ctx)
    {
        var rows = await mediator.Query(new GetMetricsQuery(G(r.WorkspaceId, "workspace_id"), G(r.RunId, "run_id"), r.Keys.ToList(), r.MaxPoints), ctx.CancellationToken);
        var res = new GetMetricsResponse();
        foreach (var g in rows.GroupBy(x => x.Key))
        {
            var s = new MetricSeries { Key = g.Key };
            s.Points.AddRange(g.Select(p => new MetricPoint { Key = p.Key, Step = p.Step, Value = p.Value, At = Timestamp.FromDateTimeOffset(p.At) }));
            res.Series.Add(s);
        }
        return res;
    }

    public override async Task<ListMetricKeysResponse> ListMetricKeys(GetRunRequest r, ServerCallContext ctx)
    {
        var res = new ListMetricKeysResponse();
        res.Metrics.AddRange((await mediator.Query(new GetMetricSummaryQuery(G(r.WorkspaceId, "workspace_id"), G(r.RunId, "run_id")), ctx.CancellationToken))
            .Select(m => new MetricInfo { Key = m.Key, Points = m.Points, LastStep = m.LastStep, Last = m.LastValue, Min = m.MinValue, Max = m.MaxValue }));
        return res;
    }

    public override async Task<Run> StopRun(GetRunRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var id = G(r.RunId, "run_id");
        // The run is marked first; the container keeps a 30 s grace to save a checkpoint, so stopping must outlive this request.
        if (await mediator.Send(new StopRunCommand(ws, id), ctx.CancellationToken) is { } job) await launcher.StopJobAsync(job, CancellationToken.None);
        return await View(ws, id, ctx);
    }

    public override async Task<RecipeTrial> StartTrial(ProjectRecipeRef r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new StartTrialCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectRecipeId, "project_recipe_id")), ctx.CancellationToken));

    public override async Task<RecipeTrial> GetTrial(GetTrialRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Query(new GetTrialQuery(G(r.WorkspaceId, "workspace_id"), G(r.TrialId, "trial_id"), LiveTrialLines), ctx.CancellationToken));

    public override async Task<RecipeTrial> CancelTrial(GetTrialRequest r, ServerCallContext ctx)
    {
        var (trial, job) = await mediator.Send(new CancelTrialCommand(G(r.WorkspaceId, "workspace_id"), G(r.TrialId, "trial_id")), ctx.CancellationToken);
        if (job is { } j) await launcher.StopJobAsync(j, CancellationToken.None);
        return ToProto(trial);
    }

    /// <summary>The "[mlp-trial]" lines the job's current attempt has logged so far (the log lives in this host, not in the database).</summary>
    private async Task<IReadOnlyList<string>> LiveTrialLines(Guid jobId)
    {
        var attempt = (await mediator.Query(new MLPlatform.ControlPlane.Application.Scheduling.ListActiveAttemptsQuery(), CancellationToken.None))
            .Where(a => a.JobId == jobId).MaxBy(a => a.Number);
        return attempt is null ? [] : await logs.TrialLinesAsync(attempt.WorkspaceId, jobId, attempt.Id, CancellationToken.None);
    }

    private static RecipeTrial ToProto(TrialView v)
    {
        var t = v.Row;
        var res = new RecipeTrial
        {
            Id = t.Id.ToString(), ProjectRecipeId = t.ProjectRecipeId.ToString(), RevisionNumber = t.RevisionNumber, State = v.State, Error = t.Error,
            StepsPerSecond = v.StepsPerS ?? 0, PeakVramMb = v.PeakVramMb ?? 0, VramEstimated = v.VramEstimated, HostId = t.HostId?.ToString() ?? "",
            HostName = v.HostName ?? "", TrainImages = v.TrainImages, ValImages = v.ValImages, RunId = t.RunId.ToString(), JobState = v.Job?.State ?? "",
            StateReason = v.Job?.StateReason ?? "", CreatedAt = Timestamp.FromDateTimeOffset(t.CreatedAt),
        };
        if (t.FinishedAt is { } f) res.FinishedAt = Timestamp.FromDateTimeOffset(f);
        res.Stages.AddRange(v.Stages.Select(s => new TrialStage { Key = s.Key, State = s.State, Seconds = s.Seconds, Detail = s.Detail }));
        return res;
    }

    public override async Task<Run> UpdateRun(UpdateRunRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var id = G(r.RunId, "run_id");
        await mediator.Send(new RenameRunCommand(ws, id, r.Name), ctx.CancellationToken);
        return await View(ws, id, ctx);
    }

    public override async Task<Empty> DeleteRun(GetRunRequest r, ServerCallContext ctx)
    {
        var prefix = await mediator.Send(new DeleteRunCommand(G(r.WorkspaceId, "workspace_id"), G(r.RunId, "run_id")), ctx.CancellationToken);
        // The record is gone; files are removed best effort (a leftover object is invisible and harmless).
        try
        {
            foreach (var o in await s3.ListAsync(MLPlatform.ControlPlane.Application.Data.Buckets.Artifacts, prefix, ctx.CancellationToken))
                await s3.DeleteAsync(MLPlatform.ControlPlane.Application.Data.Buckets.Artifacts, o.Key, ctx.CancellationToken);
        }
        catch (Exception e) when (e is MLPlatform.Storage.S3Exception or HttpRequestException or OperationCanceledException) { }
        return new Empty();
    }

    public override async Task<ListArtifactsResponse> ListArtifacts(GetRunRequest r, ServerCallContext ctx)
    {
        var res = new ListArtifactsResponse();
        res.Artifacts.AddRange((await mediator.Query(new ListRunFilesQuery(G(r.WorkspaceId, "workspace_id"), G(r.RunId, "run_id")), ctx.CancellationToken))
            .Select(f => new RunArtifact { Path = f.Path, SizeBytes = f.SizeBytes, Url = f.Url }));
        return res;
    }

    private static Project ToProto(ProjectRow p) => new()
    {
        Id = p.Id.ToString(), WorkspaceId = p.WorkspaceId.ToString(), Slug = p.Slug, Name = p.Name, Task = p.Task,
        CreatedAt = Timestamp.FromDateTimeOffset(p.CreatedAt),
    };

    public override async Task<ProjectRecipe> AdoptRecipe(AdoptRecipeRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new AdoptRecipeCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Recipe), ctx.CancellationToken));

    public override async Task<ListProjectRecipesResponse> ListProjectRecipes(ListProjectRecipesRequest r, ServerCallContext ctx)
    {
        var res = new ListProjectRecipesResponse();
        res.Recipes.AddRange((await mediator.Query(new ListProjectRecipesQuery(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<DraftState> GetDraft(ProjectRecipeRef r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new GetDraftCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectRecipeId, "project_recipe_id")), ctx.CancellationToken));

    public override async Task<DraftState> UpdateDraft(UpdateDraftRequest r, ServerCallContext ctx)
    {
        var d = r.Draft ?? throw new MLPlatform.Cqrs.ValidationException(new Dictionary<string, string> { ["draft"] = "Required" });
        Guid? Opt(string s, string f) => string.IsNullOrEmpty(s) ? null : G(s, f);
        var input = new DraftInput(G(d.ProjectRecipeId, "project_recipe_id"), d.Variant, d.ParamsJson, Opt(d.DatasetVersionId, "dataset_version_id"),
            string.IsNullOrEmpty(d.StartKind) ? "pretrained" : d.StartKind, string.IsNullOrEmpty(d.TrainKind) ? "fine_tune" : d.TrainKind,
            Opt(d.RetrainOfRunId, "retrain_of_run_id"), Opt(d.HostId, "host_id"), d.Gpus, d.Cpus, d.RunName);
        return ToProto(await mediator.Send(new UpdateDraftCommand(G(r.WorkspaceId, "workspace_id"), input, int.TryParse(r.Etag, out var e) ? e : -2), ctx.CancellationToken));
    }

    public override async Task<DraftState> ResetDraft(ResetDraftRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new ResetDraftCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectRecipeId, "project_recipe_id"), r.RevisionNumber,
            string.IsNullOrEmpty(r.RunId) ? null : G(r.RunId, "run_id"), r.Origin, r.Retrain), ctx.CancellationToken));

    public override async Task<RevisionDiff> DiffRevisions(DiffRevisionsRequest r, ServerCallContext ctx)
    {
        var v = await mediator.Query(new DiffRevisionsQuery(G(r.WorkspaceId, "workspace_id"), G(r.ProjectRecipeId, "project_recipe_id"), r.From, r.To), ctx.CancellationToken);
        var res = new RevisionDiff { From = v.From, To = v.To, ManifestChanged = v.ManifestChanged };
        res.Rows.AddRange(v.Rows.Select(x => new DraftChange { Field = x.Field, Pane = x.Pane, From = x.From, To = x.To }));
        return res;
    }

    public override async Task<Comparison> CompareRuns(CompareRunsRequest r, ServerCallContext ctx)
    {
        var v = await mediator.Query(new CompareRunsQuery(G(r.WorkspaceId, "workspace_id"), r.RunIds.Select(x => G(x, "run_ids")).ToList()), ctx.CancellationToken);
        var res = new Comparison { SameVal = v.SameVal, SameAxis = v.SameAxis };
        res.Axes.AddRange(v.Axes);
        res.Rows.AddRange(v.Rows.Select(x => { var row = new MLPlatform.Contracts.V1.CompareRow { Field = x.Field, Pane = x.Pane }; row.Values.AddRange(x.Values); return row; }));
        return res;
    }

    public override async Task<ListRecipeUsageResponse> ListRecipeUsage(ListRecipeUsageRequest r, ServerCallContext ctx)
    {
        var rows = await mediator.Query(new ListRecipeUsageQuery(G(r.WorkspaceId, "workspace_id"), r.Recipe), ctx.CancellationToken);
        var res = new ListRecipeUsageResponse();
        res.Usages.AddRange(rows.Select(x => new RecipeUsage
        {
            ProjectId = x.ProjectId.ToString(), ProjectName = x.ProjectName, ProjectRecipeId = x.ProjectRecipeId.ToString(), LatestRevision = x.LatestRevision ?? 0,
            RunCount = x.RunCount, LastRunAt = x.LastRunAt is { } at ? Timestamp.FromDateTimeOffset(at) : null,
        }));
        return res;
    }

    public override async Task<ListRevisionsResponse> ListRevisions(ProjectRecipeRef r, ServerCallContext ctx)
    {
        var (rows, names) = await mediator.Query(new ListRevisionsQuery(G(r.WorkspaceId, "workspace_id"), G(r.ProjectRecipeId, "project_recipe_id")), ctx.CancellationToken);
        var res = new ListRevisionsResponse();
        res.Revisions.AddRange(rows.Select(v => new Revision
        {
            Number = v.Number, Variant = v.Variant, ParamsJson = v.Params, Parent = v.Parent ?? 0, CreatedBy = names.GetValueOrDefault(v.CreatedBy, ""),
            CreatedAt = Timestamp.FromDateTimeOffset(v.CreatedAt), RunCount = (int)v.RunCount,
        }));
        return res;
    }

    private static ProjectRecipe ToProto(ProjectRecipeView v) => new()
    {
        Id = v.Row.Id.ToString(), ProjectId = v.Row.ProjectId.ToString(), Recipe = v.Row.OriginName, DisplayName = v.DisplayName, OriginChanged = v.OriginChanged,
        LatestRevision = v.Row.LatestRevision, CreatedAt = Timestamp.FromDateTimeOffset(v.Row.CreatedAt), ManifestJson = v.Row.OriginManifest,
    };

    private static DraftState ToProto(DraftView s)
    {
        var d = s.Draft;
        var res = new DraftState
        {
            Recipe = ToProto(s.Recipe), Etag = d.Etag.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Gate = new TrainGate { CanTrain = s.Reasons.Count == 0, CanTrial = s.CanTrial },
            Draft = new Draft
            {
                ProjectRecipeId = d.ProjectRecipeId.ToString(), Variant = d.Variant, ParamsJson = d.Params, DatasetVersionId = d.DatasetVersionId?.ToString() ?? "",
                StartKind = d.StartKind, TrainKind = d.TrainKind, RetrainOfRunId = d.RetrainOfRunId?.ToString() ?? "", HostId = d.HostId?.ToString() ?? "",
                Gpus = d.Gpus, Cpus = d.Cpus, RunName = d.RunName, BaseRevision = s.BaseRevision,
            },
        };
        res.Gate.Reasons.AddRange(s.Reasons);
        if (s.Trial is { } trial) res.Gate.Trial = ToProto(trial);
        if (s.Estimate is { } e) res.Gate.Estimate = new TrainEstimate { LowSeconds = e.LowSeconds, HighSeconds = e.HighSeconds, Source = e.Source,
            ValMeasured = e.ValMeasured, DataSeconds = e.DataSeconds };
        res.Changes.AddRange(s.Changes.Select(c => new MLPlatform.Contracts.V1.DraftChange { Field = c.Field, Pane = c.Pane, From = c.From, To = c.To }));
        if (s.Dataset is { } data)
        {
            res.Dataset = new DraftDataset
            {
                DatasetId = data.Version.DatasetId.ToString(), DatasetName = data.DatasetName, VersionNumber = data.Version.Number, FileCount = data.Version.FileCount,
                SplitState = data.State,
            };
            foreach (var (k, n) in data.Counts) res.Dataset.SplitCounts[k] = n;
        }
        return res;
    }

    private static Run ToProto(RunView v)
    {
        var r = v.Run;
        var run = new Run
        {
            Id = r.Id.ToString(), ProjectId = r.ProjectId.ToString(), Name = r.Name, State = r.State, Recipe = r.Recipe, Variant = r.Variant,
            Task = r.Task, JobId = r.JobId?.ToString() ?? "", ParamsJson = r.Params, PrimaryMetricKey = r.PrimaryMetricKey,
            PrimaryMetricValue = r.PrimaryMetricValue ?? 0, HasPrimaryMetric = r.PrimaryMetricValue is not null,
            CreatedAt = Timestamp.FromDateTimeOffset(r.CreatedAt),
            Number = r.Number, DatasetVersionId = r.DatasetVersionId?.ToString() ?? "", DatasetId = v.Dataset?.DatasetId.ToString() ?? "",
            DatasetName = v.Dataset?.DatasetName ?? "", DatasetVersionNumber = v.Dataset?.Number ?? 0, FromScratch = r.FromScratch, Gpus = r.Gpus, Cpus = r.Cpus,
            JobState = v.Job?.State ?? "", StateReason = v.Job?.StateReason ?? "", AttemptCount = v.Job?.AttemptCount ?? 0, CreatedBy = v.UserName ?? "",
        };
        // Runs from before revisions keep only the from-scratch flag; say how they started the same way as new ones.
        run.StartKind = v.Extra?.StartKind ?? (r.FromScratch ? "backbone" : "pretrained");
        if (v.Extra is { } x)
        {
            run.RevisionNumber = x.RevisionNumber ?? 0; run.TrainKind = x.TrainKind ?? ""; run.RetrainOfNumber = x.RetrainOfNumber ?? 0;
            run.HostId = x.HostId?.ToString() ?? "";
        }
        if (v.Job?.StartedAt is { } started) run.StartedAt = Timestamp.FromDateTimeOffset(started);
        if (r.EndedAt is { } ended) run.EndedAt = Timestamp.FromDateTimeOffset(ended);
        // Progress is only meaningful while the job is alive; a finished run keeps none.
        if (!RunRules.Finished(r.State) && v.Job?.Progress is { } pj && System.Text.Json.Nodes.JsonNode.Parse(pj) is System.Text.Json.Nodes.JsonObject p)
        {
            double D(string k) => p[k] is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<double>(out var d) ? d : 0;
            run.Progress = new JobProgress
            {
                Phase = p["phase"]?.ToString() ?? "", Current = D("current"), Total = D("total"), Unit = p["unit"]?.ToString() ?? "",
                Percent = D("percent"), Rate = D("rate"), EtaSeconds = D("eta_seconds"), Message = p["message"]?.ToString() ?? "",
                DetailJson = p["detail"] is System.Text.Json.Nodes.JsonObject dj ? dj.ToJsonString() : "",
            };
            if (v.Job.ProgressAt is { } at) run.Progress.At = Timestamp.FromDateTimeOffset(at);
        }
        return run;
    }
}
