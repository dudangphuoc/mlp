using System.Text.Json.Nodes;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Infrastructure;
using MLPlatform.ControlPlane.Application.Ops;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.Api.Services;

public sealed class OpsGrpc(IMediator mediator, SystemHealth health, LogStore logs, StreamBroker broker, TimeProvider time, MLPlatform.Ray.RayJobsClient ray) : OpsService.OpsServiceBase
{
    private static Guid G(string s, string f) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [f] = "Invalid UUID" });
    private static Timestamp? T(DateTimeOffset? t) => t is { } x ? Timestamp.FromDateTimeOffset(x) : null;

    public override async Task<SystemStatus> GetSystemStatus(SystemStatusRequest r, ServerCallContext ctx) =>
        await Status(G(r.WorkspaceId, "workspace_id"), true, ctx.CancellationToken);

    private async Task<SystemStatus> Status(Guid ws, bool incidents, CancellationToken ct)
    {
        var s = new SystemStatus();
        foreach (var i in health.Snapshot())
            s.Items.Add(new HealthItem { Name = SystemHealth.Label(i.Name), Group = i.Group, Status = i.Status, Message = i.Message, LastOkAt = T(i.LastOkAt),
                CheckedAt = T(i.CheckedAt), PeriodSeconds = i.PeriodSeconds });
        s.Overall = s.Items.Any(i => i.Status == "down") ? "down" : s.Items.Any(i => i.Status == "degraded") ? "degraded" : "ok";
        if (incidents)
            s.Incidents.AddRange((await mediator.Query(new SystemIncidentsQuery(ws), ct)).Select(SchedulerGrpcMap.Event));
        return s;
    }

    /// <summary>
    /// One stream per tab (20/10 §2.7): my running work, system status, unread notification count, and, when subscribed to a job,
    /// that job's new events + logs. Wakes when StreamBroker signals (workspace NOTIFY / new attempt logs), re-polls at least every 5 s; sends only on change; reconnect → full snapshot first.
    /// </summary>
    public override async Task WatchWorkspace(WatchWorkspaceRequest r, IServerStreamWriter<WorkspaceUpdate> stream, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        Guid? job = string.IsNullOrEmpty(r.SubscribeJobId) ? null : G(r.SubscribeJobId, "subscribe_job_id");
        string lastJobs = "\u0000", lastSys = "";
        long lastUnread = -1;
        var eventsAfter = DateTimeOffset.MinValue;
        var logAfter = new Dictionary<Guid, long>();
        var keys = new List<string> { StreamBroker.Ws(ws) };
        var canSeeJobs = true;
        try
        {
            while (true)
            {
                var u = new WorkspaceUpdate();
                var changed = false;
                // Roles without run.read (e.g. Reviewer) still get notifications + system status; their job list stays empty instead of failing the stream.
                List<ActiveJob> jobsMsg = [];
                if (canSeeJobs)
                    try { jobsMsg = (await mediator.Query(new ActiveJobsQuery(ws), ctx.CancellationToken)).Select(ToActive).ToList(); }
                    catch (ForbiddenException) { canSeeJobs = false; job = null; }
                var jk = string.Join("|", jobsMsg.Select(j => j.ToString()));
                if (jk != lastJobs || u.JobsChanged) { u.Jobs.AddRange(jobsMsg); u.JobsChanged = true; lastJobs = jk; changed = true; }
                var sys = await Status(ws, false, ctx.CancellationToken);
                var sk = string.Join("|", sys.Items.Select(i => i.Name + i.Status));
                if (sk != lastSys) { u.System = sys; lastSys = sk; changed = true; }
                var (_, unread) = await mediator.Query(new MLPlatform.ControlPlane.Application.Notifications.NotificationsChangedQuery(ws, time.GetUtcNow()), ctx.CancellationToken);
                if (unread != lastUnread) { u.UnreadNotifications = unread; lastUnread = unread; changed = true; }
                if (job is { } jid)
                {
                    var evs = await mediator.Query(new JobEventsAfterQuery(ws, jid, eventsAfter), ctx.CancellationToken);
                    if (evs.Count > 0) { u.Events.AddRange(evs.Select(SchedulerGrpcMap.Event)); eventsAfter = time.GetUtcNow().AddSeconds(-2); changed = true; }
                    var seen = u.Events.Select(e => e.Id).ToHashSet();
                    foreach (var aid in await mediator.Query(new JobAttemptIdsQuery(ws, jid), ctx.CancellationToken))
                    {
                        var after = logAfter.GetValueOrDefault(aid, 0);
                        var lines = logs.Tail(aid, after, 500);
                        if (lines.Count == 0) continue;
                        u.Logs.AddRange(lines.Select(l => new LogLine { TsMs = l.Ts, Text = l.Text, AttemptId = aid.ToString() }));
                        logAfter[aid] = lines[^1].Ts;
                        changed = true;
                    }
                    _ = seen;
                }
                if (changed) await stream.WriteAsync(u, ctx.CancellationToken);
                if (job is not null)
                    foreach (var aid in logAfter.Keys.Concat(await mediator.Query(new JobAttemptIdsQuery(ws, job.Value), ctx.CancellationToken)).Distinct())
                        if (!keys.Contains(StreamBroker.Attempt(aid))) keys.Add(StreamBroker.Attempt(aid));
                await broker.WaitAsync(keys, TimeSpan.FromSeconds(5), ctx.CancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    private static ActiveJob ToActive(ActiveJobRow j)
    {
        var a = new ActiveJob { Id = j.Id.ToString(), Name = j.Name, Kind = j.Kind, State = j.State, StateReason = j.StateReason ?? "" };
        if (j.Progress is { } pj && JsonNode.Parse(pj) is JsonObject p)
        {
            a.Phase = p["phase"]?.ToString() ?? "";
            a.Percent = p["percent"] is JsonValue pv && pv.TryGetValue<double>(out var d) ? d : 0;
            a.EtaSeconds = p["eta_seconds"] is JsonValue ev && ev.TryGetValue<double>(out var e) ? e : 0;
            a.Message = p["message"]?.ToString() ?? "";
        }
        (a.Alert, a.AlertText) = j switch
        {
            { AttemptState: "suspect" } => (true, "Host disconnected; waiting for it to return"),
            { SignalLostAt: not null } => (true, "Signal lost"),
            { StalledAt: not null } => (true, "May be stalled"),
            { State: "failed" } => (true, "Failed"),
            _ => (false, ""),
        };
        return a;
    }

    /// <summary>Ray state API + cluster status merged per node and mapped to the platform's machines (label mlp.io/host-id).</summary>
    public override async Task<RayHealth> GetRayHealth(ClusterMapRequest r, ServerCallContext ctx)
    {
        var ct = ctx.CancellationToken;
        var (hosts, _) = await mediator.Query(new ClusterMapQuery(G(r.WorkspaceId, "workspace_id")), ct);   // also checks access
        var names = hosts.ToDictionary(h => h.Id.ToString(), h => h.Name);
        var res = new RayHealth();
        var version = await ray.GetJsonAsync("api/version", ct);
        if (version is null) { res.Error = "Ray head does not answer on the dashboard port"; return res; }
        res.Reachable = true;
        res.RayVersion = version["ray_version"]?.ToString() ?? "";
        var usage = (await ray.GetJsonAsync("api/cluster_status?format=0", ct))?["data"]?["clusterStatus"]?["loadMetricsReport"];
        double U(string node, string key, int i) => usage?["usageByNode"]?[node]?[key] is JsonArray a && a.Count > i && a[i] is JsonValue v && v.TryGetValue<double>(out var x) ? x : 0;
        var nodes = (await ray.GetJsonAsync("api/v0/nodes?detail=1&limit=500", ct))?["data"]?["result"]?["result"] as JsonArray ?? [];
        foreach (var n in nodes.OfType<JsonObject>())
        {
            var id = n["node_id"]?.ToString() ?? "";
            var hostId = n["labels"]?["mlp.io/host-id"]?.ToString() ?? "";
            long Ms(string k) => n[k] is JsonValue v && v.TryGetValue<long>(out var x) ? x : 0;
            var rn = new RayNode
            {
                NodeId = id, Ip = n["node_ip"]?.ToString() ?? "", HostId = hostId, HostName = names.GetValueOrDefault(hostId, ""), IsHead = n["is_head_node"]?.GetValue<bool>() == true,
                State = n["state"]?.ToString() ?? "", StateMessage = n["state_message"]?.ToString() ?? "",
                CpuUsed = U(id, "CPU", 0), CpuTotal = U(id, "CPU", 1), GpuUsed = U(id, "GPU", 0), GpuTotal = U(id, "GPU", 1),
                MemoryUsed = U(id, "memory", 0), MemoryTotal = U(id, "memory", 1), ObjectStoreUsed = U(id, "objectStoreMemory", 0), ObjectStoreTotal = U(id, "objectStoreMemory", 1),
                IdleSeconds = usage?["idleTimeMap"]?[id] is JsonValue iv && iv.TryGetValue<double>(out var idle) ? idle / 1000 : 0,
            };
            if (Ms("start_time_ms") > 0) rn.StartedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeMilliseconds(Ms("start_time_ms")));
            if (Ms("end_time_ms") > 0) rn.EndedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeMilliseconds(Ms("end_time_ms")));
            res.Nodes.Add(rn);
        }
        var pgs = (await ray.GetJsonAsync("api/v0/placement_groups?detail=1&limit=200", ct))?["data"]?["result"]?["result"] as JsonArray ?? [];
        foreach (var g in pgs.OfType<JsonObject>())
            res.PlacementGroups.Add(new RayPlacementGroup
            {
                Id = g["placement_group_id"]?.ToString() ?? "", Name = g["name"]?.ToString() ?? "", State = g["state"]?.ToString() ?? "",
                Strategy = g["strategy"]?.ToString() ?? "", BundlesJson = g["bundles"]?.ToJsonString() ?? "[]",
            });
        var pending = new JsonObject { ["resources"] = usage?["resourceDemand"]?.DeepClone(), ["placement_groups"] = usage?["pgDemand"]?.DeepClone() };
        res.PendingJson = pending.ToJsonString();
        return res;
    }

    public override async Task<ClusterMap> GetClusterMap(ClusterMapRequest r, ServerCallContext ctx)
    {
        var (hosts, attempts) = await mediator.Query(new ClusterMapQuery(G(r.WorkspaceId, "workspace_id")), ctx.CancellationToken);
        var res = new ClusterMap();
        foreach (var h in hosts)
        {
            var mh = new MapHost { Id = h.Id.ToString(), Name = h.Name, State = h.State, LastSeenAt = T(h.LastSeenAt), MetricsAt = T(h.MetricsAt), RecoveringJobs = (int)h.Recovering };
            HostInfo? info = h.HostInfo is { Length: > 0 } b ? HostInfo.Parser.ParseFrom(b) : null;
            if (info is not null) { mh.Cpus = info.LogicalCpus; mh.MemoryBytes = (long)info.MemoryBytes; }
            var mine = attempts.Where(a => a.HostId == h.Id).ToList();
            HostMetrics? m = null;
            if (h.LastMetrics is { } lm) try { m = HostMetrics.Parser.ParseJson(lm); } catch (Exception e) when (e is Google.Protobuf.InvalidJsonException or InvalidOperationException) { }
            if (m is not null)
            {
                mh.CpuPercent = m.CpuPercent; mh.MemoryUsedBytes = (long)m.MemoryUsedBytes;
                foreach (var c in m.Containers)
                {
                    var a = mine.FirstOrDefault(x => c.Name.StartsWith("mlp-w-" + x.AttemptId, StringComparison.Ordinal));
                    mh.Containers.Add(new MapContainer { Name = c.Name, CpuPercent = c.CpuPercent, MemoryBytes = (long)c.MemoryBytes,
                        AttemptId = a?.AttemptId.ToString() ?? "", JobId = a?.JobId.ToString() ?? "", JobName = a?.JobName ?? "" });
                }
            }
            foreach (var g in info?.Gpus ?? [])
            {
                var s = m?.Gpus.FirstOrDefault(x => x.Uuid == g.Uuid);
                var on = mine.Where(x => x.GpuUuids.Contains(g.Uuid)).ToList();
                long reserved = 0;
                foreach (var x in on)
                {
                    var need = MLPlatform.ControlPlane.Application.Scheduling.GpuNeed.Mb(MLPlatform.ControlPlane.Application.Scheduling.JobSpecDto.Parse(x.Spec));
                    reserved += need > 0 ? (long)need << 20 : (long)g.MemoryBytes;
                }
                mh.Gpus.Add(new MapGpu { Uuid = g.Uuid, Name = g.Name, MemoryBytes = (long)g.MemoryBytes, UtilPercent = s?.UtilPercent ?? 0,
                    MemoryUsedBytes = (long)(s?.MemoryUsedBytes ?? 0), JobId = on.FirstOrDefault()?.JobId.ToString() ?? "",
                    JobName = string.Join(", ", on.Select(x => x.JobName).Distinct()), ReservedBytes = Math.Min(reserved, (long)g.MemoryBytes) });
            }
            res.Hosts.Add(mh);
        }
        return res;
    }
}

internal static class SchedulerGrpcMap
{
    public static OpsEvent Event(EventRow e) => new()
    {
        Id = e.Id.ToString(), At = Timestamp.FromDateTimeOffset(e.At), Source = e.Source, ResourceType = e.ResourceType, ResourceId = e.ResourceId?.ToString() ?? "",
        Kind = e.Kind, Severity = e.Severity, Message = e.Message, DataJson = e.Data,
    };
}
