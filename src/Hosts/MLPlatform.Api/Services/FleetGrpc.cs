using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.Api.Infrastructure;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Empty = MLPlatform.Contracts.V1.Empty;
using Host = MLPlatform.Contracts.V1.Host;

namespace MLPlatform.Api.Services;

public sealed class FleetGrpc(IMediator mediator, AgentRegistry agents, IConfiguration cfg, AgentUpgrader upgrader) : FleetService.FleetServiceBase
{
    private static Guid G(string s, string field) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { [field] = "Invalid UUID" });

    public override async Task<Enrollment> CreateEnrollment(CreateEnrollmentRequest r, ServerCallContext ctx)
    {
        var e = await mediator.Send(new CreateEnrollmentCommand(G(r.WorkspaceId, "workspace_id"), r.Os.ToString().ToLowerInvariant(),
            r.DataPath, r.ModelsPath), ctx.CancellationToken);
        var cp = cfg["PublicUrl"] ?? "https://localhost";
        var cmd = r.Os == OsFamily.Windows
            ? $"iwr {cp}/install/agent.ps1 -UseBasicParsing | iex; Install-MlpAgent -Token {e.Token} -Data '{r.DataPath}' -Models '{r.ModelsPath}'"
            : $"curl -fsSL {cp}/install/agent.sh | sudo sh -s -- --token {e.Token} --data '{r.DataPath}' --models '{r.ModelsPath}'";
        return new Enrollment { Id = e.Id.ToString(), Token = e.Token, InstallCommand = cmd, ExpiresAt = Timestamp.FromDateTimeOffset(e.ExpiresAt) };
    }

    public override async Task<ListHostsResponse> ListHosts(ListHostsRequest r, ServerCallContext ctx)
    {
        var res = new ListHostsResponse();
        var ws = G(r.WorkspaceId, "workspace_id");
        var vpn = (await mediator.Query(new MLPlatform.ControlPlane.Application.Fleet.ListHostVpnQuery(ws), ctx.CancellationToken)).ToDictionary(v => v.Id);
        foreach (var h in await mediator.Query(new ListHostsQuery(ws), ctx.CancellationToken))
        {
            var p = ToProto(h);
            if (vpn.TryGetValue(h.Id, out var v))
            {
                p.VpnStatus = v.VpnStatus; p.VpnMessage = v.VpnMessage ?? "";
                if (v.VpnCheckedAt is { } at) p.VpnCheckedAt = Timestamp.FromDateTimeOffset(at);
            }
            res.Hosts.Add(p);
        }
        return res;
    }

    public override async Task<Host> GetHost(GetHostRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Query(new GetHostQuery(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id")), ctx.CancellationToken));

    public override async Task<HostActivity> GetHostActivity(GetHostRequest r, ServerCallContext ctx)
    {
        var host = await mediator.Query(new GetHostQuery(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id")), ctx.CancellationToken);
        var res = new HostActivity { Online = agents.TryGet(host.Id, out var s) };
        if (!res.Online) { res.LogError = "The machine is offline: its agent log cannot be read now"; return res; }
        res.AgentVersion = s.AgentVersion;
        res.ConnectedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(s.ConnectedAt);
        foreach (var c in s.Trace.Reverse())
            res.Commands.Add(new AgentCommandTrace
            {
                Id = c.Id, Kind = c.Kind, Detail = c.Detail, State = c.State, Message = c.Message, LimitSeconds = (int)Math.Min(int.MaxValue, c.Limit.TotalSeconds),
                StartedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(c.Started),
                FinishedAt = c.Finished is { } f ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(f) : null,
            });
        var log = await s.SendCommandAsync(new AgentCommand { FileRead = new FileRead { Path = "agent.log", MaxBytes = 48 * 1024 } }, TimeSpan.FromSeconds(10), ctx.CancellationToken);
        if (!log.Ok) res.LogError = log.ErrorReason == "FLEET_COMMAND_TIMEOUT" ? "The agent did not answer within 10 s" : $"{log.ErrorReason}: {log.Message}";
        else if (!log.Exists) res.LogError = "The agent has no agent.log yet";
        else res.AgentLog = System.Text.Encoding.UTF8.GetString(log.Data.Span);
        return res;
    }

    public override async Task<BrowsePathResult> BrowseHostPath(BrowseHostPathRequest r, ServerCallContext ctx)
    {
        var host = await mediator.Query(new GetHostQuery(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id")), ctx.CancellationToken);
        if (!agents.TryGet(host.Id, out var s))
            throw new UnavailableException("FLEET_HOST_OFFLINE", "Host is offline");
        var res = await s.SendCommandAsync(new AgentCommand { BrowsePath = new BrowsePath { RelativePath = r.RelativePath } },
            TimeSpan.FromSeconds(15), ctx.CancellationToken);
        if (!res.Ok) throw new PreconditionException(string.IsNullOrEmpty(res.ErrorReason) ? "FLEET_COMMAND_FAILED" : res.ErrorReason, res.Message);
        return res.BrowsePath;
    }

    public override async Task<Empty> DisconnectHost(GetHostRequest r, ServerCallContext ctx)
    {
        var hostId = G(r.HostId, "host_id");
        await mediator.Send(new DisconnectHostCommand(G(r.WorkspaceId, "workspace_id"), hostId), ctx.CancellationToken);
        if (agents.TryGet(hostId, out var s))
            await s.Conn.SendAsync(new ControlToAgent { Command = new AgentCommand { CommandId = Guid.NewGuid().ToString("N"), Disconnect = new Disconnect() } });
        return new Empty();
    }

    public override async Task<ListDataCachesResponse> ListDataCaches(ListDataCachesRequest r, ServerCallContext ctx)
    {
        var rows = await mediator.Query(new MLPlatform.ControlPlane.Application.Ops.ListDataCachesQuery(G(r.WorkspaceId, "workspace_id"),
            string.IsNullOrEmpty(r.HostId) ? null : G(r.HostId, "host_id")), ctx.CancellationToken);
        var res = new ListDataCachesResponse();
        res.Caches.AddRange(rows.Select(c => new DataCache
        {
            HostId = c.HostId.ToString(), DatasetId = c.DatasetId.ToString(), CacheFiles = c.CacheFiles, CacheBytes = c.CacheBytes, FreeBytes = c.FreeBytes,
            LastUsedAt = Timestamp.FromDateTimeOffset(c.LastUsedAt), LastJobId = c.LastJobId.ToString(), InUse = c.InUse,
        }));
        return res;
    }

    /// <summary>Deletes a machine's cache volume of one dataset (the next job on it downloads the dataset again).</summary>
    public override async Task<Empty> DeleteDataCache(DeleteDataCacheRequest r, ServerCallContext ctx)
    {
        var hostId = G(r.HostId, "host_id");
        if (!agents.TryGet(hostId, out var s)) throw new UnavailableException("FLEET_HOST_OFFLINE", "Host is offline");
        var volume = await mediator.Send(new MLPlatform.ControlPlane.Application.Ops.DeleteDataCacheCommand(G(r.WorkspaceId, "workspace_id"), hostId,
            G(r.DatasetId, "dataset_id")), ctx.CancellationToken);
        // No container carries this label: only the volume is removed.
        var rm = new ContainerRemove { Label = "mlp.none=" + Guid.NewGuid().ToString("N") };
        rm.Volumes.Add(volume);
        var res = await s.SendCommandAsync(new AgentCommand { ContainerRemove = rm }, TimeSpan.FromMinutes(2), ctx.CancellationToken);
        if (!res.Ok) throw new PreconditionException(string.IsNullOrEmpty(res.ErrorReason) ? "FLEET_COMMAND_FAILED" : res.ErrorReason, res.Message);
        return new Empty();
    }

    public override async Task<DataTransferEstimate> EstimateDataTransfer(EstimateDataTransferRequest r, ServerCallContext ctx)
    {
        var (files, bytes, hosts) = await mediator.Query(new MLPlatform.ControlPlane.Application.Ops.EstimateDataTransferQuery(G(r.WorkspaceId, "workspace_id"),
            G(r.DatasetVersionId, "dataset_version_id")), ctx.CancellationToken);
        var res = new DataTransferEstimate { Files = files, Bytes = bytes };
        res.Hosts.AddRange(hosts.Select(h => new HostTransferEstimate
            { HostId = h.HostId.ToString(), CachedFiles = h.CachedFiles, CachedBytes = h.CachedBytes, FreeBytes = h.FreeBytes, Known = h.Known }));
        return res;
    }

    public override async Task<ListJobDataSyncsResponse> ListJobDataSyncs(ListJobDataSyncsRequest r, ServerCallContext ctx)
    {
        var rows = await mediator.Query(new MLPlatform.ControlPlane.Application.Ops.ListJobDataSyncsQuery(G(r.WorkspaceId, "workspace_id"), G(r.JobId, "job_id")),
            ctx.CancellationToken);
        var res = new ListJobDataSyncsResponse();
        res.Syncs.AddRange(rows.Select(x => new DataSync
        {
            Id = x.Id.ToString(), AttemptId = x.AttemptId.ToString(), HostId = x.HostId.ToString(), DatasetId = x.DatasetId.ToString(), Files = x.Files,
            ReusedFiles = x.ReusedFiles, ReusedBytes = x.ReusedBytes, FetchedFiles = x.FetchedFiles, FetchedBytes = x.FetchedBytes, UncachedFiles = x.UncachedFiles,
            Retries = x.Retries, Seconds = x.Seconds, CacheFiles = x.CacheFiles, CacheBytes = x.CacheBytes, FreeBytes = x.FreeBytes, At = Timestamp.FromDateTimeOffset(x.At),
        }));
        return res;
    }

    /// <summary>D-38 OTA: upgrade the agent on the selected hosts (empty = every online host on a different version).</summary>
    public override async Task<ListAgentUpgradesResponse> UpgradeAgents(UpgradeAgentsRequest r, ServerCallContext ctx)
    {
        var ws = G(r.WorkspaceId, "workspace_id");
        await upgrader.RequestAsync(mediator, ws, r.HostIds.Select(h => G(h, "host_ids")).ToList(), r.Force, ctx.CancellationToken);
        return await ListAgentUpgrades(new ListAgentUpgradesRequest { WorkspaceId = r.WorkspaceId }, ctx);
    }

    public override async Task<ListAgentUpgradesResponse> ListAgentUpgrades(ListAgentUpgradesRequest r, ServerCallContext ctx)
    {
        var res = new ListAgentUpgradesResponse();
        var rows = await mediator.Query(new ListAgentUpgradesQuery(G(r.WorkspaceId, "workspace_id"),
            string.IsNullOrEmpty(r.HostId) ? null : G(r.HostId, "host_id")), ctx.CancellationToken);
        res.Upgrades.AddRange(rows.Select(u => new AgentUpgrade
        {
            Id = u.Id.ToString(), HostId = u.HostId.ToString(), HostName = u.HostName, FromVersion = u.FromVersion, ToVersion = u.ToVersion,
            State = u.State, Phase = u.Phase, Message = u.Message, CreatedAt = Timestamp.FromDateTimeOffset(u.CreatedAt),
            FinishedAt = u.FinishedAt is { } f ? Timestamp.FromDateTimeOffset(f) : null,
        }));
        return res;
    }

    private Host ToProto(HostRow h) => new()
    {
        LatestAgentVersion = upgrader.LatestVersion ?? "",
        UpdateAvailable = upgrader.LatestVersion is { } lv && h.State != "offline" && h.AgentVersion != lv,
        Id = h.Id.ToString(), WorkspaceId = h.WorkspaceId.ToString(), Name = h.Name, State = h.State,
        StateReason = h.StateReason ?? "", OverlayIp = h.OverlayIp.Split('/')[0], AgentVersion = h.AgentVersion,
        HostInfo = h.HostInfo is { Length: > 0 } b ? HostInfo.Parser.ParseFrom(b) : null,
        LastSeenAt = Timestamp.FromDateTimeOffset(h.LastSeenAt),
    };
}
