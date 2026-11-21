using MLPlatform.ControlPlane.Application.Common;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Infrastructure;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.Contracts.V1;
using Empty = MLPlatform.Contracts.V1.Empty;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Services;

public sealed class LabelingGrpc(IMediator mediator, CurrentUser user, HermesLabelChecker hermes, AiRuntime ai, LabelProposer proposer, HermesQuality quality,
    IHttpClientFactory http, ILogger<LabelingGrpc> log)
    : LabelingService.LabelingServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    private static Timestamp? Ts(DateTimeOffset? t) => t is { } v ? Timestamp.FromDateTimeOffset(v) : null;

    public override async Task<LabelSchema> CreateSchemaVersion(CreateSchemaVersionRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreateLabelSchemaCommand(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id"), r.Task, r.ClassesJson), ctx.CancellationToken));

    public override async Task<LabelSchema> GetSchema(GetSchemaRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Query(new GetLabelSchemaQuery(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id"), r.Version), ctx.CancellationToken));

    public override async Task<LabelingProject> CreateProject(CreateLabelingProjectRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreateLabelingProjectCommand(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id"), r.Name,
            r.GuidelineMd, r.ReviewMode, r.LockTtlMin, string.IsNullOrEmpty(r.VersionId) ? null : G(r.VersionId, "version_id"), r.AutoAcceptThreshold), ctx.CancellationToken));

    public override async Task<ListLabelingProjectsResponse> ListProjects(ListLabelingProjectsRequest r, ServerCallContext ctx)
    {
        var res = new ListLabelingProjectsResponse();
        Guid? ds = string.IsNullOrEmpty(r.DatasetId) ? null : G(r.DatasetId, "dataset_id");
        res.Projects.AddRange((await mediator.Query(new ListLabelingProjectViewsQuery(G(r.WorkspaceId, "workspace_id"), ds), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<LabelingProject> SetProjectState(SetLabelingProjectStateRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new SetLabelingProjectStateCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.State), ctx.CancellationToken));

    public override async Task<LabelingProjectStats> GetProjectStats(LabelingProjectRef r, ServerCallContext ctx)
    {
        var (p, stats) = await mediator.Query(new GetLabelingStatsQuery(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id")), ctx.CancellationToken);
        var view = (await mediator.Query(new ListLabelingProjectViewsQuery(p.WorkspaceId, p.DatasetId), ctx.CancellationToken)).FirstOrDefault(v => v.Project.Id == p.Id);
        var res = new LabelingProjectStats { Project = view is null ? ToProto(p) : ToProto(view), Total = stats.Sum(s => s.N) };
        foreach (var s in stats) res.ByStatus[s.Status] = s.N;
        return res;
    }

    public override async Task<ListLabelingMembersResponse> ListProjectMembers(LabelingProjectRef r, ServerCallContext ctx) =>
        await Members(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), ctx);

    public override async Task<ListLabelingMembersResponse> AddProjectMember(LabelingMemberRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        await mediator.Send(new AddLabelingMemberCommand(ws, pid, G(r.UserId, "user_id"), r.Role, r.MaxOpenItems), ctx.CancellationToken);
        return await Members(ws, pid, ctx);
    }

    public override async Task<ListLabelingMembersResponse> RemoveProjectMember(LabelingMemberRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        await mediator.Send(new RemoveLabelingMemberCommand(ws, pid, G(r.UserId, "user_id"), r.Role), ctx.CancellationToken);
        return await Members(ws, pid, ctx);
    }

    private async Task<ListLabelingMembersResponse> Members(Guid ws, Guid pid, ServerCallContext ctx)
    {
        var res = new ListLabelingMembersResponse();
        var done = (await mediator.Query(new ListLabelingMemberProgressQuery(ws, pid), ctx.CancellationToken)).ToDictionary(d => d.UserId);
        foreach (var m in await mediator.Query(new ListLabelingMembersQuery(ws, pid), ctx.CancellationToken))
        {
            var row = new LabelingMember { UserId = m.UserId.ToString(), Username = m.Username, Role = m.Role, MaxOpenItems = m.MaxOpenItems, OpenItems = m.OpenItems };
            if (m.Role == "annotator" && done.TryGetValue(m.UserId, out var d))
            {
                row.Submitted = d.Submitted; row.Accepted = d.Accepted; row.Returned = d.Returned;
                row.LastSubmittedAt = d.LastAt is { } at ? Timestamp.FromDateTimeOffset(at) : null;
            }
            res.Members.Add(row);
        }
        return res;
    }

    public override Task<NextItemResponse> NextItem(LabelingProjectRef r, ServerCallContext ctx) => Next(r, false, ctx);
    public override Task<NextItemResponse> NextReviewItem(LabelingProjectRef r, ServerCallContext ctx) => Next(r, true, ctx);

    private async Task<NextItemResponse> Next(LabelingProjectRef r, bool review, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        var v = await mediator.Send(new NextLabelItemCommand(ws, pid, review), ctx.CancellationToken);
        if (v is not null && await ImportExistingAsync(ws, pid, v.State.Path, ctx.CancellationToken))
            v = await mediator.Query(new GetLabelItemQuery(ws, pid, v.State.Path), ctx.CancellationToken);
        return v is null ? new NextItemResponse { Empty = true } : new NextItemResponse { Item = ToProto(v) };
    }

    /// <summary>
    /// First time an item of a version is opened: the YOLO label file next to the image (imported datasets, exported label versions) becomes
    /// editable labels. A source that cannot be read leaves the item unlabeled; the next open tries again.
    /// </summary>
    private async Task<bool> ImportExistingAsync(Guid ws, Guid pid, string path, CancellationToken ct)
    {
        var url = await mediator.Query(new ResolveItemLabelFileQuery(ws, pid, path), ct);
        if (url is null) return false;
        string text;
        try
        {
            using var res = await http.CreateClient("eval").GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return false;
            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length > 1_000_000) return false;
            text = System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { log.LogInformation("Reading labels of {Path} failed: {M}", path, e.Message); return false; }
        return await mediator.Send(new ImportItemLabelsCommand(ws, pid, path, text), ct) > 0;
    }

    public override async Task<LabelItem> ClaimItem(LabelItemRef r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        await ImportExistingAsync(ws, pid, r.Path, ctx.CancellationToken);
        return ToProto(await mediator.Send(new ClaimLabelItemCommand(ws, pid, r.Path), ctx.CancellationToken));
    }

    public override async Task<SyncProjectResponse> SyncProject(LabelingProjectRef r, ServerCallContext ctx) =>
        new() { Added = await mediator.Send(new SyncLabelingProjectCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id")), ctx.CancellationToken) };

    public override async Task<Empty> DeleteProject(LabelingProjectRef r, ServerCallContext ctx)
    {
        await mediator.Send(new DeleteLabelingProjectCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id")), ctx.CancellationToken);
        return new Empty();
    }

    public override async Task<ListMyItemsResponse> ListMyItems(LabelingProjectRef r, ServerCallContext ctx)
    {
        var res = new ListMyItemsResponse();
        res.Items.AddRange((await mediator.Query(new ListMyLabelItemsQuery(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id")), ctx.CancellationToken))
            .Select(i => new MyItem { Path = i.Path, Status = i.Status, Etag = i.Revision }));
        return res;
    }

    public override async Task<LabelItem> GetItem(LabelItemRef r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        await ImportExistingAsync(ws, pid, r.Path, ctx.CancellationToken);
        return ToProto(await mediator.Query(new GetLabelItemQuery(ws, pid, r.Path), ctx.CancellationToken));
    }

    public override async Task<LockInfo> HeartbeatLock(LabelItemRef r, ServerCallContext ctx) =>
        new() { ExpiresAt = Timestamp.FromDateTimeOffset(await mediator.Send(new HeartbeatLabelLockCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Path), ctx.CancellationToken)) };

    public override async Task<LabelItemRef> SaveDraft(SaveAnnotationsRequest r, ServerCallContext ctx)
    {
        await mediator.Send(new SaveLabelDraftCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Path, Inputs(r)), ctx.CancellationToken);
        return new LabelItemRef { WorkspaceId = r.WorkspaceId, ProjectId = r.ProjectId, Path = r.Path };
    }

    public override async Task<SaveAnnotationsResponse> SaveAnnotations(SaveAnnotationsRequest r, ServerCallContext ctx) =>
        new() { Etag = await mediator.Send(new SaveLabelAnnotationsCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Path, r.Etag, Inputs(r)), ctx.CancellationToken) };

    private static List<AnnotationInput> Inputs(SaveAnnotationsRequest r) =>
        r.Annotations.Select(a => new AnnotationInput(a.Kind, a.ClassId, a.GeometryJson, a.AttributesJson,
            string.IsNullOrEmpty(a.Source) ? "human" : a.Source, a.HasConfidence ? a.Confidence : null)).ToList();

    public override async Task<ItemStatus> SubmitItem(LabelItemRef r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        var res = new ItemStatus { Status = await mediator.Send(new SubmitLabelItemCommand(ws, pid, r.Path), ctx.CancellationToken) };
        // Hermes runs right after the submit is committed; a failing check never undoes the submit (it is recorded as an error check).
        try
        {
            if (await hermes.CheckAsync(mediator, ws, pid, r.Path, "submit", ctx.CancellationToken) is { } check)
            {
                res.Hermes = Map(check, await CanUndoAsync(ws, pid, ctx));
                if (check.Action == "auto_accepted") res.Status = "accepted";
            }
        }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Hermes check after submit failed for {Path}", r.Path); }
        return res;
    }

    // ───────── Hermes ─────────

    private async Task<bool> CanUndoAsync(Guid ws, Guid pid, ServerCallContext ctx)
    {
        var (_, _, roles) = await mediator.Query(new GetHermesStatsQuery(ws, pid), ctx.CancellationToken);
        return roles.Contains("lead") || roles.Contains("reviewer");
    }

    private static HermesCheck Map(HermesCheckRow c, bool canUndo)
    {
        var x = new HermesCheck
        {
            Id = c.Id.ToString(), Path = c.Path, Revision = c.Revision, Trigger = c.Trigger, Verdict = c.Verdict, Confidence = c.Confidence ?? -1,
            DecidedBy = c.DecidedBy, Summary = c.Summary, Suggestion = c.Suggestion ?? "", AiRunId = c.AiRunId?.ToString() ?? "", Action = c.Action,
            ActorName = c.ActorName ?? "", CreatedAt = Timestamp.FromDateTimeOffset(c.CreatedAt), UndoneByName = c.UndoneByName ?? "", UndoneAt = Ts(c.UndoneAt),
            CanUndo = canUndo && c.Action == "auto_accepted",
            Explanation = c.Explanation ?? "", ExplanationLang = c.ExplanationLang ?? "", ExplanationRunId = c.ExplanationRunId?.ToString() ?? "",
            ExplainedAt = Ts(c.ExplainedAt),
        };
        try
        {
            foreach (var n in System.Text.Json.Nodes.JsonNode.Parse(c.StepsJson) as System.Text.Json.Nodes.JsonArray ?? [])
                x.Steps.Add(new HermesStep
                {
                    Name = n?["name"]?.ToString() ?? "", Kind = n?["kind"]?.ToString() ?? "", Outcome = n?["outcome"]?.ToString() ?? "",
                    Detail = n?["detail"]?.ToString() ?? "", Ms = n?["ms"]?.GetValue<int>() ?? 0,
                });
        }
        catch (System.Text.Json.JsonException) { }
        return x;
    }

    public override async Task<HermesCheck> CheckItem(LabelItemRef r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        var check = await hermes.CheckAsync(mediator, ws, pid, r.Path, "manual", ctx.CancellationToken);
        return Map(check!, await CanUndoAsync(ws, pid, ctx));
    }

    public override async Task<ListHermesChecksResponse> ListHermesChecks(ListHermesChecksRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        var rows = await mediator.Query(new ListHermesChecksQuery(ws, pid, r.Verdict, r.Path, r.Limit), ctx.CancellationToken);
        var undo = await CanUndoAsync(ws, pid, ctx);
        var res = new ListHermesChecksResponse();
        res.Checks.AddRange(rows.Select(c => Map(c, undo)));
        return res;
    }

    public override async Task<HermesCheck> GetHermesCheck(HermesCheckRef r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        return Map(await mediator.Query(new GetHermesCheckQuery(ws, pid, G(r.CheckId, "check_id")), ctx.CancellationToken), await CanUndoAsync(ws, pid, ctx));
    }

    public override async Task<HermesCheck> UndoHermesAccept(HermesCheckRef r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        return Map(await mediator.Send(new UndoHermesAcceptCommand(ws, pid, G(r.CheckId, "check_id")), ctx.CancellationToken), true);
    }

    public override async Task<LabelingProject> SetHermesSettings(SetHermesSettingsRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new SetHermesSettingsCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Enabled, r.AutoAcceptThreshold),
            ctx.CancellationToken));

    public override async Task<HermesStats> GetHermesStats(LabelingProjectRef r, ServerCallContext ctx)
    {
        var (p, stats, roles) = await mediator.Query(new GetHermesStatsQuery(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id")), ctx.CancellationToken);
        long N(Func<HermesStatRow, bool> f) => stats.Where(f).Sum(x => x.N);
        var withConf = stats.Where(x => x.AvgConfidence is not null).ToList();
        var cat = await ai.CatalogAsync(ctx.CancellationToken);
        var ready = ai.ReadinessOf(cat).First(x => x.Role == "decision");
        var res = new HermesStats
        {
            Project = ToProto(p), Checked = N(_ => true), Passed = N(x => x.Verdict == "pass"), Flagged = N(x => x.Verdict == "flag"), Failed = N(x => x.Verdict == "fail"),
            Errors = N(x => x.Verdict == "error"), AutoAccepted = N(x => x.Action == "auto_accepted"), Undone = N(x => x.Action == "undone"),
            AvgConfidence = withConf.Count == 0 ? -1 : withConf.Sum(x => x.AvgConfidence!.Value * x.N) / withConf.Sum(x => x.N),
            DecisionState = ready.State, DecisionModel = ready.ModelLabel ?? "",
            DecisionExternal = ready.ModelId is { } dm && cat.Models.FirstOrDefault(m => m.Id == dm) is { } dmr && cat.Connections.First(c => c.Id == dmr.ConnectionId).IsExternal,
            CanUndo = roles.Contains("lead") || roles.Contains("reviewer"),
            CanManage = roles.Contains("lead") || roles.Contains("manage"),
        };
        res.DecisionReasons.AddRange(ready.Reasons);
        return res;
    }

    public override async Task<ItemStatus> SkipItem(ItemActionRequest r, ServerCallContext ctx) =>
        new() { Status = await mediator.Send(new SkipLabelItemCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Path, r.Text), ctx.CancellationToken) };

    public override async Task<ItemStatus> AcceptItem(ItemActionRequest r, ServerCallContext ctx) =>
        new() { Status = await mediator.Send(new ReviewLabelItemCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Path, true, r.Text), ctx.CancellationToken) };

    public override async Task<ItemStatus> RejectItem(ItemActionRequest r, ServerCallContext ctx) =>
        new() { Status = await mediator.Send(new ReviewLabelItemCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Path, false, r.Text), ctx.CancellationToken) };

    public override async Task<ItemStatus> ReopenItem(ItemActionRequest r, ServerCallContext ctx) =>
        new() { Status = await mediator.Send(new ReopenLabelItemCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Path, r.Text), ctx.CancellationToken) };

    public override async Task<LabelItem> AddComment(ItemActionRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        await mediator.Send(new AddLabelCommentCommand(ws, pid, r.Path, r.Text), ctx.CancellationToken);
        return ToProto(await mediator.Query(new GetLabelItemQuery(ws, pid, r.Path), ctx.CancellationToken));
    }

    public override async Task<ItemHistory> GetItemHistory(LabelItemRef r, ServerCallContext ctx)
    {
        var res = new ItemHistory();
        res.Entries.AddRange((await mediator.Query(new GetLabelHistoryQuery(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Path), ctx.CancellationToken))
            .Select(h => new HistoryEntry { Action = h.Action, Actor = h.Username, At = Timestamp.FromDateTimeOffset(h.At) }));
        return res;
    }

    public override async Task<LabelSet> CreateLabelSet(CreateLabelSetRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Send(new CreateLabelSetCommand(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id"), r.AllLabeled), ctx.CancellationToken));

    public override async Task<ListLabelSetsResponse> ListLabelSets(ListLabelSetsRequest r, ServerCallContext ctx)
    {
        var res = new ListLabelSetsResponse();
        res.LabelSets.AddRange((await mediator.Query(new ListLabelSetsQuery(G(r.WorkspaceId, "workspace_id"), G(r.DatasetId, "dataset_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    // ───────── Phase 4: explanations, quality, calibration ─────────

    public override async Task<HermesCheck> ExplainHermesCheck(ExplainHermesCheckRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        var row = await quality.ExplainAsync(mediator, ws, pid, G(r.CheckId, "check_id"), r.Lang == "en" ? "en" : "vi", r.ConfirmExternal, ctx.CancellationToken);
        return Map(row, await CanUndoAsync(ws, pid, ctx));
    }

    public override async Task<QualityReport> GetQualityReport(LabelingProjectRef r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        var rep = await mediator.Query(new GetQualityReportQuery(ws, pid), ctx.CancellationToken);
        var (_, settings, _) = await mediator.Query(new GetAssistSettingsQuery(ws, pid), ctx.CancellationToken);
        var analyst = await quality.AnalystAsync(ctx.CancellationToken);
        var res = new QualityReport
        {
            Calibration = HermesQuality.Calibrate(rep.Calibration, rep.Project.AutoAcceptThreshold), CanManage = rep.CanManage, AnalystState = analyst.State,
            AnalystModel = analyst.Model, AnalystExternal = analyst.External, AllowExternal = settings.AllowExternal, ReviewMode = rep.Project.ReviewMode,
        };
        foreach (var a in rep.Annotators)
            res.Annotators.Add(new AnnotatorQuality
            {
                AnnotatorId = a.AnnotatorId.ToString(), AnnotatorName = a.AnnotatorName ?? "", Items = a.Items, Submitted = a.Submitted, Accepted = a.Accepted,
                Rejected = a.Rejected, InReview = a.InReview, ReturnedEver = a.ReturnedEver, Checked = a.Checked, HermesPass = a.HermesPass, HermesFlag = a.HermesFlag,
                HermesFail = a.HermesFail, AutoAccepted = a.AutoAccepted, Undone = a.Undone, AvgConfidence = a.AvgConfidence ?? -1,
            });
        return res;
    }

    public override async Task<QualitySummary> SummarizeQuality(SummarizeQualityRequest r, ServerCallContext ctx) =>
        await quality.SummarizeAsync(mediator, G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Lang == "en" ? "en" : "vi", r.ConfirmExternal,
            user.UserId ?? Guid.Empty, ctx.CancellationToken);

    // ───────── AI assist ─────────

    private static SuggestionSet Map(SuggestionSetRow s)
    {
        var x = new SuggestionSet
        {
            Id = s.Id.ToString(), Path = s.Path, Revision = s.Revision, SourceKind = s.SourceKind, SourceId = s.SourceId.ToString(), SourceLabel = s.SourceLabel,
            External = s.External, State = s.State, Detail = s.Detail, AiRunId = s.AiRunId?.ToString() ?? "", LatencyMs = s.LatencyMs, JobId = s.JobId?.ToString() ?? "",
            Decision = s.Decision, CreatedByName = s.CreatedByName ?? "", CreatedAt = Timestamp.FromDateTimeOffset(s.CreatedAt), DecidedAt = Ts(s.DecidedAt),
        };
        try
        {
            foreach (var n in System.Text.Json.Nodes.JsonNode.Parse(s.ItemsJson) as System.Text.Json.Nodes.JsonArray ?? [])
                x.Items.Add(new ProposedLabel
                {
                    Kind = n?["kind"]?.ToString() ?? "", ClassId = n?["class_id"]?.GetValue<int>() ?? 0, GeometryJson = n?["geometry"]?.ToJsonString() ?? "{}",
                    Confidence = n?["confidence"] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<double>(out var d) ? d : -1, Label = n?["label"]?.ToString() ?? "",
                });
            foreach (var n in System.Text.Json.Nodes.JsonNode.Parse(s.UnmappedJson) as System.Text.Json.Nodes.JsonArray ?? [])
                x.Unmapped.Add(new UnmappedLabel { Label = n?["label"]?.ToString() ?? "", Count = n?["count"]?.GetValue<int>() ?? 0 });
        }
        catch (System.Text.Json.JsonException) { }
        return x;
    }

    private static PrelabelJob Map(PrelabelJobRow j) => new()
    {
        Id = j.Id.ToString(), SourceKind = j.SourceKind, SourceLabel = j.SourceLabel, Total = j.Total, Done = j.Done, Failed = j.Failed, State = j.State,
        Error = j.Error ?? "", CreatedByName = j.CreatedByName ?? "", CreatedAt = Timestamp.FromDateTimeOffset(j.CreatedAt), StartedAt = Ts(j.StartedAt), FinishedAt = Ts(j.FinishedAt),
    };

    private async Task<AssistSettings> SettingsAsync(Guid ws, Guid pid, ServerCallContext ctx)
    {
        var (p, s, manage) = await mediator.Query(new GetAssistSettingsQuery(ws, pid), ctx.CancellationToken);
        var schema = await mediator.Query(new GetLabelSchemaQuery(ws, p.DatasetId, p.SchemaVersion), ctx.CancellationToken);
        var res = new AssistSettings { AllowExternal = s.AllowExternal, MinConfidence = s.MinConfidence, Saved = s.Saved, CanManage = manage, ClassesJson = schema.Classes };
        foreach (var (k, v) in s.ClassMapping) res.ClassMapping[k] = v;
        foreach (var src in await proposer.ListSourcesAsync(mediator, ws, ctx.CancellationToken))
        {
            var x = new AssistSource { Kind = src.Kind, Id = src.Id.ToString(), Label = src.Label, Enabled = s.IsEnabled(src.Kind, src.Id, src.External), External = src.External,
                State = src.State, StateReason = src.Reason, Detail = src.Detail };
            x.Media.AddRange(src.Media);
            res.Sources.Add(x);
        }
        return res;
    }

    public override async Task<AssistSettings> GetAssistSettings(LabelingProjectRef r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        var res = await SettingsAsync(ws, pid, ctx);
        var (p, _, _) = await mediator.Query(new GetAssistSettingsQuery(ws, pid), ctx.CancellationToken);
        res.DatasetTask = (await mediator.Query(new MLPlatform.ControlPlane.Application.Data.GetDatasetQuery(ws, p.DatasetId), ctx.CancellationToken)).Task;
        return res;
    }

    public override async Task<AssistSettings> SaveAssistSettings(SaveAssistSettingsRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        var sources = new System.Text.Json.Nodes.JsonArray();
        foreach (var x in r.Sources) sources.Add((System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject { ["kind"] = x.Kind, ["id"] = x.Id });
        var map = new System.Text.Json.Nodes.JsonObject();
        foreach (var (k, v) in r.ClassMapping) map[k] = v;
        await mediator.Send(new SaveAssistSettingsCommand(ws, pid, sources.ToJsonString(), map.ToJsonString(), r.AllowExternal, r.MinConfidence), ctx.CancellationToken);
        return await GetAssistSettings(new LabelingProjectRef { WorkspaceId = r.WorkspaceId, ProjectId = r.ProjectId }, ctx);
    }

    public override async Task<SuggestionSet> ProposeItem(ProposeItemRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        var context = await mediator.Query(new AuthorizeProposalQuery(ws, pid, r.Path), ctx.CancellationToken);
        var (_, settings, _) = await mediator.Query(new GetAssistSettingsQuery(ws, pid), ctx.CancellationToken);
        return Map(await proposer.ProposeAsync(mediator, context, settings, r.Source.Kind, G(r.Source.Id, "source.id"), r.ConfirmExternal, "manual", null, ctx.CancellationToken));
    }

    public override async Task<ListSuggestionSetsResponse> ListSuggestionSets(ListSuggestionSetsRequest r, ServerCallContext ctx)
    {
        var rows = await mediator.Query(new ListSuggestionSetsQuery(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Path,
            string.IsNullOrEmpty(r.JobId) ? null : G(r.JobId, "job_id"), r.Limit), ctx.CancellationToken);
        var res = new ListSuggestionSetsResponse();
        res.Sets.AddRange(rows.Select(Map));
        return res;
    }

    public override async Task<SuggestionSet> DecideSuggestionSet(DecideSuggestionSetRequest r, ServerCallContext ctx) =>
        Map(await mediator.Send(new DecideSuggestionSetCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), G(r.Id, "id"), r.Decision), ctx.CancellationToken));

    public override async Task<PrelabelJob> StartPrelabel(StartPrelabelRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var pid = G(r.ProjectId, "project_id");
        var (_, settings, _) = await mediator.Query(new GetAssistSettingsQuery(ws, pid), ctx.CancellationToken);
        var sid = G(r.Source.Id, "source.id");
        var src = (await proposer.ListSourcesAsync(mediator, ws, ctx.CancellationToken)).FirstOrDefault(s => s.Kind == r.Source.Kind && s.Id == sid)
            ?? throw new NotFoundException("LABEL_SOURCE_NOT_FOUND", "Proposal source not found");
        if (!settings.IsEnabled(src.Kind, src.Id, src.External)) throw new PreconditionException("LABEL_SOURCE_NOT_ENABLED", "This source is not enabled in the project's AI assist settings");
        if (src.External && (!settings.AllowExternal || !r.ConfirmExternal))
            throw new PreconditionException("LABEL_EXTERNAL_CONFIRM", "A batch through an external API needs external APIs on for the project and an explicit confirmation");
        if (src.State == "unavailable") throw new PreconditionException("LABEL_SOURCE_UNAVAILABLE", $"{src.Label} is not available: {src.Reason}");
        return Map(await mediator.Send(new StartPrelabelCommand(ws, pid, src.Kind, src.Id, src.Label, r.Limit), ctx.CancellationToken));
    }

    public override async Task<PrelabelJob> CancelPrelabel(PrelabelJobRef r, ServerCallContext ctx) =>
        Map(await mediator.Send(new CancelPrelabelCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), G(r.JobId, "job_id")), ctx.CancellationToken));

    public override async Task<ListPrelabelJobsResponse> ListPrelabelJobs(LabelingProjectRef r, ServerCallContext ctx)
    {
        var (jobs, targets) = await mediator.Query(new ListPrelabelJobsQuery(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id")), ctx.CancellationToken);
        var res = new ListPrelabelJobsResponse { Targets = targets };
        res.Jobs.AddRange(jobs.Select(Map));
        return res;
    }

    private static LabelSchema ToProto(SchemaRow s) => new()
        { Id = s.Id.ToString(), DatasetId = s.DatasetId.ToString(), Version = s.Version, Task = s.Task, ClassesJson = s.Classes, CreatedAt = Timestamp.FromDateTimeOffset(s.CreatedAt) };

    private static LabelingProject ToProto(ProjectRow p) => new()
    {
        Id = p.Id.ToString(), DatasetId = p.DatasetId.ToString(), Name = p.Name, GuidelineMd = p.GuidelineMd, SchemaVersion = p.SchemaVersion,
        ReviewMode = p.ReviewMode, LockTtlMin = p.LockTtlMin, State = p.State, CreatedAt = Timestamp.FromDateTimeOffset(p.CreatedAt),
        VersionId = p.VersionId?.ToString() ?? "", AutoAcceptThreshold = p.AutoAcceptThreshold, HermesEnabled = p.HermesEnabled,
    };

    private static LabelingProject ToProto(ProjectView v)
    {
        var p = ToProto(v.Project);
        p.DatasetName = v.DatasetName;
        if (v.Summary is not { } s) return p;
        p.Total = s.Total; p.Accepted = s.Accepted; p.InReview = s.InReview; p.Unlabeled = s.Unlabeled; p.MyOpen = s.MyOpen; p.MyReturned = s.MyReturned;
        p.MyRoles.AddRange(s.MyRoles.Split(',', StringSplitOptions.RemoveEmptyEntries));
        return p;
    }

    public override async Task<ListLabelItemsResponse> ListItems(ListLabelItemsRequest r, ServerCallContext ctx)
    {
        var page = await mediator.Query(new ListLabelItemsQuery(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.Status, r.Search, r.Mine, r.Limit, r.Offset), ctx.CancellationToken);
        var res = new ListLabelItemsResponse { Total = page.Total };
        res.Items.AddRange(page.Items.Select(i => new LabelItemRow
        {
            Path = i.Path, Status = i.Status, Labels = i.Labels, SkipReason = i.SkipReason ?? "", Url = page.Urls?.GetValueOrDefault(i.Path) ?? "",
            Annotator = i.AnnotatorId is { } a ? page.Names.GetValueOrDefault(a, "") : "", Reviewer = i.ReviewerId is { } rv ? page.Names.GetValueOrDefault(rv, "") : "",
            SubmittedAt = i.SubmittedAt is { } st ? Timestamp.FromDateTimeOffset(st) : null, ReviewedAt = i.ReviewedAt is { } rt ? Timestamp.FromDateTimeOffset(rt) : null,
        }));
        return res;
    }

    public override async Task<ExportLabelsResponse> ExportLabels(ExportLabelsRequest r, ServerCallContext ctx)
    {
        var e = await mediator.Send(new ExportLabeledVersionCommand(G(r.WorkspaceId, "workspace_id"), G(r.ProjectId, "project_id"), r.AllLabeled, r.Note), ctx.CancellationToken);
        return new ExportLabelsResponse
        {
            DatasetId = e.Version.DatasetId.ToString(), VersionId = e.Version.Id.ToString(), VersionNumber = e.Version.Number,
            Items = e.Items, Boxes = e.Boxes, OtherLabels = e.OtherLabels, FileCount = e.Version.FileCount,
        };
    }

    private static LabelSet ToProto(LabelSetRow l) => new()
    {
        Id = l.Id.ToString(), Number = l.Number, SchemaVersion = l.SchemaVersion, ItemCount = l.ItemCount, AnnotationCount = l.AnnotationCount,
        IncludeStatus = l.IncludeStatus, CreatedAt = Timestamp.FromDateTimeOffset(l.CreatedAt),
    };

    private LabelItem ToProto(ItemView v)
    {
        var s = v.State;
        var item = new LabelItem
        {
            ProjectId = s.ProjectId.ToString(), Path = s.Path, Status = s.Status, AnnotatorId = s.AnnotatorId?.ToString() ?? "",
            ReviewerId = s.ReviewerId?.ToString() ?? "", Etag = s.Revision, LockExpiresAt = Ts(s.LockExpiresAt),
            LockedByMe = s.LockOwner is not null && s.LockOwner == user.UserId, Url = v.Url, ClassesJson = v.Classes,
            DraftJson = s.DraftAnnotations ?? "", DraftBy = s.DraftBy?.ToString() ?? "", DraftSavedAt = Ts(s.DraftSavedAt), SkipReason = s.SkipReason ?? "",
        };
        foreach (var a in v.Annotations)
        {
            var pa = new Annotation { Id = a.Id.ToString(), Kind = a.Kind, ClassId = a.ClassId, GeometryJson = a.Geometry, AttributesJson = a.Attributes, Source = a.Source };
            if (a.Confidence is { } c) pa.Confidence = c;
            item.Annotations.Add(pa);
        }
        item.Comments.AddRange(v.Comments.Select(c => new LabelComment
            { Id = c.Id.ToString(), Author = c.Author, Body = c.Body, Resolved = c.Resolved, CreatedAt = Timestamp.FromDateTimeOffset(c.CreatedAt) }));
        return item;
    }
}
