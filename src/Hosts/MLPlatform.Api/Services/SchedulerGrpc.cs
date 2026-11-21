using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Infrastructure;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Empty = MLPlatform.Contracts.V1.Empty;

namespace MLPlatform.Api.Services;

public sealed class SchedulerGrpc(IMediator mediator, LogStore logs, WorkloadLauncher launcher) : SchedulerService.SchedulerServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });

    public override async Task<Job> SubmitJob(SubmitJobRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        var spec = new JobSpecDto
        {
            Image = r.Image, Command = [.. r.Command], Cpus = r.Cpus <= 0 ? 1 : r.Cpus, Gpus = r.Gpus, MemoryBytes = r.MemoryBytes,
            Hosts = [.. r.HostIds.Select(h => G(h, "host_ids"))], Nnodes = r.Nnodes <= 0 ? Math.Max(1, r.HostIds.Count) : r.Nnodes,
            Env = new Dictionary<string, string>(r.Env), Network = string.IsNullOrEmpty(r.Network) ? null : r.Network,
        };
        var id = await mediator.Send(new SubmitJobCommand(ws, string.IsNullOrEmpty(r.Kind) ? "train" : r.Kind, r.Name, spec,
            r.Priority == 0 ? 50 : r.Priority), ctx.CancellationToken);
        return ToProto(await mediator.Query(new GetJobQuery(ws, id), ctx.CancellationToken));
    }

    public override async Task<ListJobsResponse> ListJobs(ListJobsRequest r, ServerCallContext ctx)
    {
        var res = new ListJobsResponse();
        res.Jobs.AddRange((await mediator.Query(new ListJobsQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken)).Select(ToProto));
        return res;
    }

    public override async Task<Job> GetJob(GetJobRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Query(new GetJobQuery(G(r.WorkspaceId, "workspace_id"), G(r.JobId, "job_id")), ctx.CancellationToken));

    public override async Task<Job> CancelJob(GetJobRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var id = G(r.JobId, "job_id");
        await mediator.Send(new MLPlatform.ControlPlane.Application.Ops.CancelJobByUserCommand(ws, id), ctx.CancellationToken);
        // Marks the job cancelled, stops its container (if any) and frees its reservation; a machine that does not answer cannot fail the request.
        try { await launcher.StopJobAsync(id, CancellationToken.None); }
        catch (Exception e) when (e is TimeoutException or InvalidOperationException or IOException or ObjectDisposedException) { }
        return ToProto(await mediator.Query(new GetJobQuery(ws, id), ctx.CancellationToken));
    }

    public override async Task<Job> ResumeJob(GetJobRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var id = G(r.JobId, "job_id");
        await mediator.Send(new MLPlatform.ControlPlane.Application.Ops.ResumeJobCommand(ws, id), ctx.CancellationToken);
        // A training run follows its job: without this it would stay "failed" while the job trains again.
        await mediator.Send(new MLPlatform.ControlPlane.Application.Tracking.SyncRunStateCommand(id, "queued"), ctx.CancellationToken);
        return ToProto(await mediator.Query(new GetJobQuery(ws, id), ctx.CancellationToken));
    }

    public override async Task<JobLogs> GetJobLogs(GetJobLogsRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var job = G(r.JobId, "job_id");
        var attempts = await mediator.Query(new MLPlatform.ControlPlane.Application.Ops.JobAttemptIdsQuery(ws, job), ctx.CancellationToken);
        if (!string.IsNullOrEmpty(r.AttemptId)) attempts = attempts.Where(a => a == G(r.AttemptId, "attempt_id")).ToList();
        var tail = r.Tail is > 0 and <= 20_000 ? r.Tail : 500;
        var res = new JobLogs();
        foreach (var a in attempts)
        {
            var lines = logs.Tail(a, r.AfterTsMs, tail);
            // Memory holds only the newest lines (none after an api restart): a first load completes them from storage.
            if (r.AfterTsMs == 0 && lines.Count < tail)
            {
                var stored = await logs.ReadPersistedAsync(ws, job, a, tail, ctx.CancellationToken);
                var seen = stored.ToHashSet();
                lines = stored.Concat(lines.Where(l => !seen.Contains(l))).OrderBy(l => l.Ts).TakeLast(tail).ToList();
            }
            res.Lines.AddRange(lines.Select(l => new LogLine { TsMs = l.Ts, Text = l.Text, AttemptId = a.ToString() }));
        }
        return res;
    }

    public override async Task<Job> MoveAttemptNow(GetJobRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var id = G(r.JobId, "job_id");
        await mediator.Send(new MLPlatform.ControlPlane.Application.Ops.MoveAttemptNowCommand(ws, id), ctx.CancellationToken);
        return ToProto(await mediator.Query(new GetJobQuery(ws, id), ctx.CancellationToken));
    }

    public override async Task<Job> ExtendGrace(ExtendGraceRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id"); var id = G(r.JobId, "job_id");
        await mediator.Send(new MLPlatform.ControlPlane.Application.Ops.ExtendGraceCommand(ws, id, r.Minutes), ctx.CancellationToken);
        return ToProto(await mediator.Query(new GetJobQuery(ws, id), ctx.CancellationToken));
    }

    public override async Task<JobOps> GetJobOps(GetJobRequest r, ServerCallContext ctx)
    {
        var v = await mediator.Query(new MLPlatform.ControlPlane.Application.Ops.GetJobOpsQuery(G(r.WorkspaceId, "workspace_id"), G(r.JobId, "job_id")), ctx.CancellationToken);
        static Timestamp? T(DateTimeOffset? t) => t is { } x ? Timestamp.FromDateTimeOffset(x) : null;
        var res = new JobOps();
        if (v.Progress?.Progress is { } pj && System.Text.Json.Nodes.JsonNode.Parse(pj) is System.Text.Json.Nodes.JsonObject p)
        {
            double D(string k) => p[k] is System.Text.Json.Nodes.JsonValue jv && jv.TryGetValue<double>(out var d) ? d : 0;
            res.Progress = new JobProgress { Phase = p["phase"]?.ToString() ?? "", Current = D("current"), Total = D("total"), Unit = p["unit"]?.ToString() ?? "",
                Percent = D("percent"), Rate = D("rate"), EtaSeconds = D("eta_seconds"), Message = p["message"]?.ToString() ?? "", At = T(v.Progress.ProgressAt),
                DetailJson = p["detail"] is System.Text.Json.Nodes.JsonObject dj ? dj.ToJsonString() : "" };
        }
        foreach (var a in v.Attempts)
        {
            var ai = new AttemptInfo { Id = a.Id.ToString(), Number = a.Number, State = a.State, ExitCode = a.ExitCode ?? 0, ExitReason = a.ExitReason ?? "",
                Message = a.Message ?? "", StartedAt = T(a.StartedAt), FinishedAt = T(a.FinishedAt), LastHeartbeatAt = T(a.LastHeartbeatAt),
                SuspectUntil = T(a.SuspectUntil), ResumedFrom = a.ResumedFrom ?? "" };
            ai.HostIds.AddRange(a.HostIds.Select(h => h.ToString()));
            res.Attempts.Add(ai);
        }
        res.Checkpoints.AddRange(v.Checkpoints.Select(c => new CheckpointInfo { Id = c.Id.ToString(), AttemptId = c.AttemptId.ToString(), HostId = c.HostId.ToString(),
            Step = c.Step, Name = c.Name, Location = c.Location, SizeBytes = c.SizeBytes, CreatedAt = T(c.CreatedAt) }));
        foreach (var m in await mediator.Query(new MLPlatform.ControlPlane.Application.Ops.JobMetricsQuery(G(r.WorkspaceId, "workspace_id"), G(r.JobId, "job_id")), ctx.CancellationToken))
            res.Metrics.Add(new AttemptMetric { AttemptId = m.AttemptId.ToString(), At = T(m.At), CpuPercent = m.CpuPercent, MemoryBytes = m.MemoryBytes,
                GpuUtil = m.GpuUtil ?? 0, VramBytes = m.VramBytes ?? 0 });
        res.Events.AddRange(v.Events.Select(e => new OpsEvent { Id = e.Id.ToString(), At = T(e.At), Source = e.Source, ResourceType = e.ResourceType,
            ResourceId = e.ResourceId?.ToString() ?? "", Kind = e.Kind, Severity = e.Severity, Message = e.Message, DataJson = e.Data }));
        return res;
    }

    private static Job ToProto(JobRow j) => new()
    {
        Id = j.Id.ToString(), WorkspaceId = j.WorkspaceId.ToString(), Kind = j.Kind, Name = j.Name, State = j.State,
        StateReason = j.StateReason ?? "", AttemptCount = j.AttemptCount, LastExitReason = j.LastExitReason ?? "",
        CreatedAt = Timestamp.FromDateTimeOffset(j.CreatedAt),
        StartedAt = j.StartedAt is { } s ? Timestamp.FromDateTimeOffset(s) : null,
        FinishedAt = j.FinishedAt is { } f ? Timestamp.FromDateTimeOffset(f) : null,
    };
}
