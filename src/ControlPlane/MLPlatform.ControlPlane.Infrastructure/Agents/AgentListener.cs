using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using MLPlatform.AgentProtocol;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Infrastructure;

/// <summary>TCP(+TLS) listener :7443 for the agent channel (20/04 §3).</summary>
public sealed class AgentListener(IServiceScopeFactory scopes, AgentRegistry registry, WorkloadLauncher launcher, DataChannelBroker dataChannels, LogStore logs, SystemHealth health, IConfiguration cfg,
    TimeProvider time, ILogger<AgentListener> log) : BackgroundService
{
    /// <summary>SHA-256 (hex) of the agent channel certificate, printed into the install command for the agent to pin
    /// (P0-8).</summary>
    public static string? CertPin(IConfiguration cfg) =>
        cfg["Agent:CertPath"] is { } path && File.Exists(path)
            ? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(X509CertificateLoader.LoadPkcs12FromFile(path, cfg["Agent:CertPassword"]).RawData))
            : null;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var port = int.TryParse(cfg["Agent:Port"], out var p) ? p : 7443;
        var certPath = cfg["Agent:CertPath"];
        var plaintext = string.Equals(cfg["Agent:AllowPlaintext"], "true", StringComparison.OrdinalIgnoreCase);
        if (certPath is null && !plaintext)
        {
            log.LogWarning("Agent channel disabled: MLP__Agent__CertPath is missing (or set AllowPlaintext=true for dev)");
            return;
        }
        var cert = certPath is null ? null : X509CertificateLoader.LoadPkcs12FromFile(certPath, cfg["Agent:CertPassword"]);
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        health.Component("agent-channel", true, $"Nghe :{port}");
        log.LogInformation("Agent channel listening on :{Port} ({Mode})", port, cert is null ? "plaintext" : "TLS");
        while (!stop.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(stop);
            _ = Task.Run(() => HandleAsync(client, cert, stop), stop);
        }
    }

    private async Task HandleAsync(TcpClient client, X509Certificate2? cert, CancellationToken stop)
    {
        using var _ = client;
        client.NoDelay = true;
        Stream stream = client.GetStream();
        try
        {
            if (cert is not null)
            {
                var ssl = new SslStream(stream);
                await ssl.AuthenticateAsServerAsync(cert, false, false);
                stream = ssl;
            }
            using var helloCts = CancellationTokenSource.CreateLinkedTokenSource(stop);
            helloCts.CancelAfter(TimeSpan.FromSeconds(5));
            var hello = await Framing.ReadAsync(stream, ConnectionHello.Parser, helloCts.Token);
            if (hello is null) return;
            switch (hello.KindCase)
            {
                case ConnectionHello.KindOneofCase.Register:
                case ConnectionHello.KindOneofCase.Enroll:
                    await ControlChannelAsync(stream, hello, stop);
                    break;
                case ConnectionHello.KindOneofCase.DataChannel:
                    await dataChannels.AcceptAsync(hello.DataChannel, stream, stop);
                    break;
                default:
                    return;
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or InvalidDataException or InvalidProtocolBufferException)
        {
            log.LogDebug("Agent connection closed early: {Msg}", e.Message);
        }
    }

    private async Task ControlChannelAsync(Stream stream, ConnectionHello hello, CancellationToken stop)
    {
        if (await HandshakeAsync(stream, hello, stop) is not { } outcome) return;
        var (result, hostId, workspaceId, agentVersion) = outcome;
        ApplyClusterSettings(result);
        await Framing.WriteAsync(stream, new ControlToAgent { HelloResult = result }, stop);

        var conn = new Connection<AgentToControl, ControlToAgent>(stream, AgentToControl.Parser,
            () => new ControlToAgent { Heartbeat = new Heartbeat { At = Connection<AgentToControl, ControlToAgent>.Now(time) } },
            log, oddStreamIds: false);
        var session = CreateSession(hello, hostId, workspaceId, agentVersion, result, conn);
        registry.Add(session);
        if (string.Equals(cfg["Ray:AutoStartNodes"], "true", StringComparison.OrdinalIgnoreCase))
            _ = Task.Run(() => StartRayNodeAsync(session, hello, result.OverlayIp, stop), stop);
        log.LogInformation("Agent {Host} connected ({Count} hosts online)", hostId, registry.Count);
        try
        {
            await conn.RunAsync(msg => OnMessage(session, msg, agentVersion, stop), stop);
        }
        finally
        {
            registry.Remove(session);
            session.Pending.FailAll("FLEET_HOST_OFFLINE");
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new MarkHostOfflineCommand(hostId, "Agent channel lost"), CancellationToken.None);
            log.LogInformation("Agent {Host} disconnected", hostId);
        }
    }

    private sealed record HandshakeOutcome(HelloResult Result, Guid HostId, Guid WorkspaceId, string AgentVersion);

    /// <summary>Enrolls or registers the agent; on rejection writes the failed hello result and returns null.</summary>
    private async Task<HandshakeOutcome?> HandshakeAsync(Stream stream, ConnectionHello hello, CancellationToken stop)
    {
        await using var scope = scopes.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        try
        {
            return hello.KindCase == ConnectionHello.KindOneofCase.Enroll
                ? await EnrollAsync(mediator, hello.Enroll, stop)
                : await RegisterAsync(mediator, hello.Register, stop);
        }
        catch (AppException ex)
        {
            await Framing.WriteAsync(stream, new ControlToAgent { HelloResult = new HelloResult { Ok = false, ErrorReason = ex.Reason } }, stop);
            return null;
        }
    }

    private static async Task<HandshakeOutcome> EnrollAsync(IMediator mediator, EnrollHello e, CancellationToken stop)
    {
        var r = await mediator.Send(new EnrollAgentCommand(e.EnrollToken, e.PreviousCredential,
            e.HostInfo?.MachineName ?? "host", e.AgentVersion, e.WgPublicKey, e.HostInfo?.ToByteArray() ?? []), stop);
        var result = new HelloResult { Ok = true, HostId = r.HostId.ToString(), Credential = r.Credential, OverlayIp = r.OverlayIp };
        return new HandshakeOutcome(result, r.HostId, r.WorkspaceId, e.AgentVersion);
    }

    private static async Task<HandshakeOutcome> RegisterAsync(IMediator mediator, RegisterHello g, CancellationToken stop)
    {
        var h = await mediator.Send(new AgentRegisterCommand(g.Credential, g.AgentVersion, g.HostInfo?.ToByteArray(), g.WgPublicKey), stop);
        var result = new HelloResult { Ok = true, HostId = h.Id.ToString(), OverlayIp = h.OverlayIp.Split('/')[0] };
        return new HandshakeOutcome(result, h.Id, h.WorkspaceId, g.AgentVersion);
    }

    private void ApplyClusterSettings(HelloResult result)
    {
        result.RayHead = cfg["Ray:HeadAddress"] ?? "10.88.0.1:6379";
        result.NodeImage = cfg["Images:Node"] ?? "";
        result.MlpNetImage = cfg["Images:MlpNet"] ?? "";
        result.WgPeer = new WireGuardPeer
        {
            PublicKey = Overlay.HubPublicKey(cfg) ?? "", Endpoint = cfg["WireGuard:Endpoint"] ?? "", AllowedIps = "10.88.0.0/16",
        };
    }

    private AgentSession CreateSession(ConnectionHello hello, Guid hostId, Guid workspaceId, string agentVersion, HelloResult result,
        Connection<AgentToControl, ControlToAgent> conn)
    {
        var helloInfo = hello.KindCase == ConnectionHello.KindOneofCase.Enroll ? hello.Enroll.HostInfo : hello.Register.HostInfo;
        return new AgentSession(hostId, workspaceId, conn)
            { Info = helloInfo, AgentVersion = helloInfo?.AgentVersion is { Length: > 0 } hv ? hv : agentVersion, ConnectedAt = time.GetUtcNow(),
              Hello = hello, OverlayIp = result.OverlayIp };
    }

    private async ValueTask OnMessage(AgentSession s, AgentToControl msg, string agentVersion, CancellationToken ct)
    {
        switch (msg.BodyCase)
        {
            case AgentToControl.BodyOneofCase.CommandResult:
                s.Pending.Complete(msg.CommandResult);
                break;
            case AgentToControl.BodyOneofCase.LogChunk when Guid.TryParse(msg.LogChunk.AttemptId, out var la):
                await logs.AppendAsync(la, msg.LogChunk.TsMs, msg.LogChunk.Lines);
                break;
            case AgentToControl.BodyOneofCase.HostMetrics:
                await OnHostMetricsAsync(s, msg.HostMetrics, ct);
                break;
            case AgentToControl.BodyOneofCase.ContainerSnapshot:
                _ = Task.Run(() => ReconcileAsync(s, msg.ContainerSnapshot), CancellationToken.None);
                break;
            case AgentToControl.BodyOneofCase.ContainerEvent when msg.ContainerEvent.Action == "die":
                _ = Task.Run(() => OnContainerExitAsync(s, msg.ContainerEvent.Container), CancellationToken.None);
                break;
            case AgentToControl.BodyOneofCase.HostInfo:
            case AgentToControl.BodyOneofCase.Heartbeat:
                await using (var scope = scopes.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new AgentHeartbeatCommand(s.HostId,
                        msg.BodyCase == AgentToControl.BodyOneofCase.HostInfo ? msg.HostInfo.ToByteArray() : null, agentVersion), ct);
                break;
        }
    }

    private async Task OnHostMetricsAsync(AgentSession s, HostMetrics hm, CancellationToken ct)
    {
        await using (var ds = scopes.CreateAsyncScope())
            if (await ds.ServiceProvider.GetRequiredService<IMediator>().Send(new SetHostDockerCommand(s.HostId, !hm.DockerDown, hm.DockerError), ct))
            {
                if (hm.DockerDown) log.LogWarning("Host {Host}: Docker is not running: {Err}", s.HostId, hm.DockerError);
                else
                {
                    log.LogInformation("Host {Host}: Docker is back; rebuilding network + Ray node", s.HostId);
                    if (s.Hello is { } h0 && string.Equals(cfg["Ray:AutoStartNodes"], "true", StringComparison.OrdinalIgnoreCase))
                        _ = Task.Run(() => StartRayNodeAsync(s, h0, s.OverlayIp, CancellationToken.None), CancellationToken.None);
                }
            }
        await using (var ms = scopes.CreateAsyncScope())
            await ms.ServiceProvider.GetRequiredService<IMediator>().Send(new MLPlatform.ControlPlane.Application.Ops.RecordHostMetricsCommand(s.HostId,
                Google.Protobuf.JsonFormatter.Default.Format(hm),
                hm.Containers.Select(c => (c.Name, c.CpuPercent, (long)c.MemoryBytes)).ToList(),
                hm.Gpus.Select(g => (g.Uuid, g.UtilPercent, (long)g.MemoryUsedBytes)).ToList()), ct);
    }

    /// <summary>An agent that just (re)connected reports running/exited containers (20/10 §4.3, §4.7): finalize exited
    /// containers, re-adopt live ones.</summary>
    private async Task ReconcileAsync(AgentSession s, ContainerSnapshot snap)
    {
        try
        {
            foreach (var c in snap.Containers.Where(c => c.State == "exited" && c.Labels.ContainsKey("mlp.attempt")))
                await OnContainerExitAsync(s, c);
            await using var scope = scopes.CreateAsyncScope();
            var running = snap.Containers.Where(c => c.State == "running" && c.Labels.ContainsKey("mlp.attempt"))
                .Select(c => Guid.TryParse(c.Labels["mlp.attempt"], out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();
            var (stop, lost) = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
                new MLPlatform.ControlPlane.Application.Ops.ReconcileHostCommand(s.HostId, running), CancellationToken.None);
            foreach (var id in stop)
                await s.SendCommandAsync(new AgentCommand { ContainerStop = new ContainerStop { Label = "mlp.attempt=" + id, GraceSeconds = 30 } }, TimeSpan.FromMinutes(2), CancellationToken.None);
            foreach (var id in lost)
                await launcher.FailAsync(id, "container_lost", "The host came back but the container is gone (power loss/reboot)", CancellationToken.None);
            if (running.Count + stop.Count + lost.Count > 0)
                log.LogInformation("Agent {Host}: re-adopted {Run} container(s), stopped {Stop}, lost {Lost}", s.HostId, running.Count - stop.Count, stop.Count, lost.Count);
        }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Workload reconciliation for {Host} failed", s.HostId); }
    }

    /// <summary>Workload container exited (die event or in the snapshot) → finalize the attempt, then remove the
    /// container (the control plane has recorded it).</summary>
    private async Task OnContainerExitAsync(AgentSession s, ContainerState c)
    {
        if (!c.Labels.ContainsKey("mlp.attempt")) return;
        try
        {
            var state = await launcher.OnExitAsync(c, CancellationToken.None);
            var rm = new ContainerRemove { Name = c.Name };
            if (state is "succeeded" or "failed" or "cancelled" or "stopped" && c.Labels.TryGetValue("mlp.volume", out var vol)) rm.Volumes.Add(vol);
            await s.SendCommandAsync(new AgentCommand { ContainerRemove = rm }, TimeSpan.FromMinutes(2), CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "Handling exit of container {Name} failed", c.Name); }
    }

    /// <summary>After hello: build the overlay network (if there is a hub), then the Ray node; keep it if already
    /// running to spec (reconnecting does not lose the placement group).</summary>
    private async Task StartRayNodeAsync(AgentSession s, ConnectionHello hello, string overlayIp, CancellationToken ct)
    {
        var info = hello.KindCase == ConnectionHello.KindOneofCase.Enroll ? hello.Enroll.HostInfo : hello.Register.HostInfo;
        var overlay = await HostContainers.EnsureNetworkAsync(s, overlayIp, cfg, scopes, log, ct);
        if (!overlay && Overlay.HubPublicKey(cfg) is not null)
        {
            log.LogWarning("Host {Host} has not joined the VPN; Ray node not built, retrying in 60 s", s.HostId);
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(60), ct); await StartRayNodeAsync(s, hello, overlayIp, ct); }
                catch (OperationCanceledException) { }
                catch (Exception e) { log.LogWarning(e, "VPN retry for {Host} failed", s.HostId); }
            }, ct);
            return;
        }
        var node = HostContainers.RayNode(s, info, cfg, overlay, overlayIp);
        var res = await s.SendCommandAsync(new AgentCommand { ContainerApply = new ContainerApply { Spec = node, KeepIfSame = true } }, TimeSpan.FromMinutes(15), ct);
        if (res.Ok) log.LogInformation("Ray node on {Host} {State}", s.HostId, res.Kept ? "running, kept as is" : "started");
        else log.LogWarning("Ray node on {Host} failed: {Reason} {Msg}", s.HostId, res.ErrorReason, res.Message);
    }
}
