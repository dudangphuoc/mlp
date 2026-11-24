using MLPlatform.ControlPlane.Infrastructure;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Empty = MLPlatform.Contracts.V1.Empty;

namespace MLPlatform.Api.Grpc;

public sealed class TrackingGrpc(IMediator mediator, MLPlatform.Storage.S3Client s3,
    MLPlatform.ControlPlane.Infrastructure.LogStore logs, IConfiguration cfg, SecretBox box) : TrackingService.TrackingServiceBase
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

    public override async Task<OpenCodeRunResponse> OpenCodeRun(OpenCodeRunRequest r, ServerCallContext ctx)
    {
        var code = r.Code ?? new RunCode();
        var codeJson = System.Text.Json.JsonSerializer.Serialize(new CodeJson(code.GitRemote, code.Commit, code.Dirty, code.Script, [.. code.Argv], code.Python, code.Branch),
            CodeJsonContext.Default.CodeJson);
        var remote = r.Remote is { } rr
            ? new RemoteCodeRun(rr.Machine, rr.Gpus, rr.Image, rr.SnapshotSha256, rr.Workdir, [.. rr.Command], rr.Env.ToDictionary(), rr.Requirements,
                code.GitRemote, rr.BaseCommit, [.. rr.Deleted], rr.Data.ToDictionary(),
                rr.DebugToken.Length > 0 ? new RemoteDebug(rr.DebugToken, rr.DebugParentPid, rr.DebugRoot) : null)
            : null;
        var opened = await mediator.Send(new OpenCodeRunCommand(r.Workspace, r.Project, r.Name, r.ParamsJson, r.HostName, codeJson, remote), ctx.CancellationToken);
        return new OpenCodeRunResponse
        {
            Run = await View(opened.WorkspaceId, opened.RunId, ctx), WorkspaceId = opened.WorkspaceId.ToString(),
            WorkspaceSlug = opened.WorkspaceSlug, ProjectSlug = opened.ProjectSlug,
            WebUrl = $"{(cfg["PublicUrl"] ?? "").TrimEnd('/')}/w/{opened.WorkspaceSlug}/projects/{opened.ProjectId}/runs/{opened.RunId}",
        };
    }

    public override async Task<Empty> HeartbeatCodeRun(GetRunRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new HeartbeatCodeRunCommand(G(r.WorkspaceId, "workspace_id"), G(r.RunId, "run_id")), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<Run> FinishCodeRun(FinishCodeRunRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var id = G(r.RunId, "run_id");
        await mediator.Send(new FinishCodeRunCommand(ws, id, r.ExitCode), ctx.CancellationToken);
        return await View(ws, id, ctx);
    }

    public override async Task<Empty> SetCodeRunParams(SetCodeRunParamsRequest r, ServerCallContext ctx)
    {
        var workload = WorkloadToken(ctx);
        var ws = workload is null ? G(r.WorkspaceId, "workspace_id") : Guid.Empty;
        await mediator.Send(new SetCodeRunParamsCommand(ws, G(r.RunId, "run_id"), r.ParamsJson, workload), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<CodeSnapshotUpload> CreateCodeSnapshotUpload(CreateCodeSnapshotUploadRequest r, ServerCallContext ctx)
    {
        var u = await mediator.Send(new CreateCodeSnapshotUploadCommand(r.Workspace, r.Sha256, r.SizeBytes, r.Data), ctx.CancellationToken);
        return new CodeSnapshotUpload { WorkspaceId = u.WorkspaceId.ToString(), WorkspaceSlug = u.WorkspaceSlug, Exists = u.Exists, Url = u.Url };
    }

    public override async Task<MatchCodeSourceResponse> MatchCodeSource(MatchCodeSourceRequest r, ServerCallContext ctx) =>
        new() { UrlPrefix = await mediator.Query(new MatchCodeSourceQuery(r.Workspace, r.GitRemote), ctx.CancellationToken) };

    public override async Task<ListCodeSourcesResponse> ListCodeSources(ListCodeSourcesRequest r, ServerCallContext ctx)
    {
        var res = new ListCodeSourcesResponse();
        res.Sources.AddRange((await mediator.Query(new ListCodeSourcesQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<CodeSource> CreateCodeSource(CreateCodeSourceRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        var id = await mediator.Send(new CreateCodeSourceCommand(ws, r.UrlPrefix, string.IsNullOrWhiteSpace(r.Token) ? null : box.Seal(r.Token.Trim())), ctx.CancellationToken);
        return (await mediator.Query(new ListCodeSourcesQuery(ws), ctx.CancellationToken)).Where(s => s.Id == id).Select(ToProto).First();
    }

    public override async Task<Empty> DeleteCodeSource(DeleteCodeSourceRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new DeleteCodeSourceCommand(G(r.WorkspaceId, "workspace_id"), G(r.Id, "id")), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<ListCodeEntriesResponse> ListCodeEntries(ListCodeEntriesRequest r, ServerCallContext ctx)
    {
        var res = new ListCodeEntriesResponse();
        res.Entries.AddRange((await mediator.Query(new ListCodeEntriesQuery(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<CodeEntry> CreateCodeEntry(CreateCodeEntryRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var project = G(r.ProjectId, "project_id");
        var id = await mediator.Send(new CreateCodeEntryCommand(ws, project, r.Name, r.GitRemote, r.Ref, r.Script, r.Machine, r.Gpus, r.Image), ctx.CancellationToken);
        return (await mediator.Query(new ListCodeEntriesQuery(ws, project), ctx.CancellationToken)).Where(e => e.Id == id).Select(ToProto).First();
    }

    public override async Task<Empty> DeleteCodeEntry(DeleteCodeEntryRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new DeleteCodeEntryCommand(G(r.WorkspaceId, "workspace_id"), G(r.Id, "id")), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<Run> RunCodeEntry(RunCodeEntryRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        var opened = await mediator.Send(new RunCodeEntryCommand(ws, G(r.EntryId, "entry_id"), r.Ref, r.Machine, r.Params.ToDictionary()), ctx.CancellationToken);
        return await View(ws, opened.RunId, ctx);
    }

    private static CodeEntry ToProto(CodeEntryRow e) => new()
    {
        Id = e.Id.ToString(), ProjectId = e.ProjectId.ToString(), Name = e.Name, GitRemote = e.GitRemote, Ref = e.Ref, Script = e.Script, Machine = e.Machine,
        Gpus = e.Gpus, Image = e.Image, CreatedAt = Timestamp.FromDateTimeOffset(e.CreatedAt),
    };

    private static CodeSource ToProto(CodeSourceRow s) => new()
    {
        Id = s.Id.ToString(), UrlPrefix = s.UrlPrefix, HasToken = s.SecretEnc is not null, CreatedAt = Timestamp.FromDateTimeOffset(s.CreatedAt),
    };

    public override async Task<ResolvedModel> ResolveModelRef(ResolveRefRequest r, ServerCallContext ctx)
    {
        var workload = WorkloadToken(ctx);
        var m = await mediator.Send(new ResolveModelRefCommand(workload is null ? G(r.WorkspaceId, "workspace_id") : Guid.Empty,
            workload is null ? null : G(r.RunId, "run_id"), workload, r.Ref), ctx.CancellationToken);
        var res = new ResolvedModel { Repo = m.Repo, Revision = m.Revision };
        res.Files.AddRange(m.Files.Select(f => new ResolvedModelFile
            { Path = f.Path, SizeBytes = f.Size, Url = f.Url, Content = f.Content is null ? Google.Protobuf.ByteString.Empty : Google.Protobuf.ByteString.CopyFrom(f.Content) }));
        return res;
    }

    public override async Task<ResolvedDataset> ResolveDataset(ResolveRefRequest r, ServerCallContext ctx)
    {
        var workload = WorkloadToken(ctx);
        var d = await mediator.Send(new ResolveCodeDatasetCommand(workload is null ? G(r.WorkspaceId, "workspace_id") : Guid.Empty,
            workload is null ? null : G(r.RunId, "run_id"), workload, r.Ref), ctx.CancellationToken);
        var res = new ResolvedDataset { VersionId = d.VersionId.ToString(), Name = d.Name, Version = d.Version };
        res.Files.AddRange(d.Files.Select(f => new ResolvedDatasetFile { Path = f.Path, SizeBytes = f.Size, Url = f.Url }));
        return res;
    }

    private static string? WorkloadToken(ServerCallContext ctx) =>
        ctx.RequestHeaders.GetValue("authorization") is { } h && h.StartsWith("Bearer mlpj_", StringComparison.Ordinal) ? h[7..] : null;

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
        await mediator.Send(new StopRunCommand(ws, id), ctx.CancellationToken);
        return await View(ws, id, ctx);
    }

    public override async Task<RecipeTrial> StartTrial(ProjectRecipeRef r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new StartTrialCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectRecipeId, "project_recipe_id")), ctx.CancellationToken));

    public override async Task<RecipeTrial> GetTrial(GetTrialRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Query(new GetTrialQuery(G(r.WorkspaceId, "workspace_id"), G(r.TrialId, "trial_id"), LiveTrialLines), ctx.CancellationToken));

    public override async Task<RecipeTrial> CancelTrial(GetTrialRequest r, ServerCallContext ctx)
    {
        var (trial, _) = await mediator.Send(new CancelTrialCommand(G(r.WorkspaceId, "workspace_id"), G(r.TrialId, "trial_id")), ctx.CancellationToken);
        return ToProto(trial);
    }

    /// <summary>The "[mlp-trial]" lines the job's current attempt has logged so far (the log lives in this host, not in
    /// the database).</summary>
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

    private static void AddCodeRun(Run run, RunExtraRow x)
    {
        run.Origin = x.Origin; run.HostName = x.HostName ?? "";
        if (x.ExitCode is { } exit) { run.ExitCode = exit; run.HasExitCode = true; }
        if (x.HeartbeatAt is { } beat) run.HeartbeatAt = Timestamp.FromDateTimeOffset(beat);
        if (x.Code is not { Length: > 0 } json || System.Text.Json.JsonSerializer.Deserialize(json, CodeJsonContext.Default.CodeJson) is not { } code) return;
        run.Code = new RunCode { GitRemote = code.GitRemote ?? "", Commit = code.Commit ?? "", Dirty = code.Dirty, Script = code.Script ?? "",
            Python = code.Python ?? "", Branch = code.Branch ?? "" };
        run.Code.Argv.AddRange(code.Argv ?? []);
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
        run.StartKind = v.Extra?.StartKind ?? (r.FromScratch ? "backbone" : "pretrained");
        if (v.Extra is { } x)
        {
            run.RevisionNumber = x.RevisionNumber ?? 0; run.TrainKind = x.TrainKind ?? ""; run.RetrainOfNumber = x.RetrainOfNumber ?? 0;
            run.HostId = x.HostId?.ToString() ?? "";
            AddCodeRun(run, x);
        }
        if (v.Job?.StartedAt is { } started) run.StartedAt = Timestamp.FromDateTimeOffset(started);
        else if (v.Extra?.Origin == "code") run.StartedAt = run.CreatedAt;
        if (r.EndedAt is { } ended) run.EndedAt = Timestamp.FromDateTimeOffset(ended);
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
