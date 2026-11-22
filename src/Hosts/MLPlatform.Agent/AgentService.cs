using System.Net.Security;
using System.Net.Sockets;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MLPlatform.AgentProtocol;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Agent;

/// <summary>Maintains the control channel: enroll the first time, register afterwards, reconnect with backoff + jitter
/// (20/04 §3).</summary>
public sealed class AgentService(AgentState state, CommandHandler commands, TimeProvider time, ILogger<AgentService> log,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    /// <summary>Version = agent source hash stamped by agent-builder at build time (OTA compares it with the control
    /// plane's current build); manual builds → "dev".</summary>
    public static readonly string Version =
        (typeof(AgentService).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute)?.InformationalVersion.Split('+')[0] is { Length: > 0 } v && v != "1.0.0" ? v : "dev";
    private static readonly int[] Backoff = [2, 4, 8, 16, 30];

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        log.LogInformation("MLPlatform agent {Version} ({Rid}, service {Svc})", Version, System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, state.Options.ServiceManager);
        var attempt = 0;
        while (!stop.IsCancellationRequested)
        {
            Outcome outcome;
            try { outcome = await ConnectOnceAsync(stop); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                log.LogWarning("Connection to control plane failed: {Type}: {Msg}", e.GetType().Name, e.Message);
                outcome = Outcome.Failed;
            }
            if (outcome == Outcome.Fatal) { lifetime.StopApplication(); return; }
            if (outcome == Outcome.Connected) attempt = 0;
            var baseSec = attempt < Backoff.Length ? Backoff[attempt] : 10;
            attempt++;
            var jitter = 1 + (Random.Shared.NextDouble() * 0.6 - 0.3);
            var delay = TimeSpan.FromSeconds(baseSec * jitter);
            log.LogInformation("Reconnecting in {Delay:F1}s", delay.TotalSeconds);
            await Task.Delay(delay, time, stop);
        }
    }

    private enum Outcome { Connected, Failed, Fatal }

    private async Task<Outcome> ConnectOnceAsync(CancellationToken stop)
    {
        var o = state.Options;
        var parts = o.ControlPlane.Split(':');
        using var tcp = new TcpClient { NoDelay = true };
        if (await OpenStreamAsync(tcp, o, parts, stop) is not { } stream) return Outcome.Failed;

        var info = await HostProbe.CollectAsync(o, stop);
        info.AgentStateDir = state.Dir;
        info.FsRoots.AddRange(Ops.PathGuard.Roots(o, state.Dir));
        var hello = BuildHello(o, info);
        await Framing.WriteAsync(stream, hello, stop);
        var first = await Framing.ReadAsync(stream, ControlToAgent.Parser, stop);
        if (first?.HelloResult is not { } res) return Outcome.Failed;
        if (!res.Ok) return HandleRejection(o, res);
        AcceptHello(o, hello, res);
        HostOwnership.HostId = res.HostId;

        await using var conn = new Connection<ControlToAgent, AgentToControl>(stream, ControlToAgent.Parser,
            () => new AgentToControl { Heartbeat = new Heartbeat { At = Timestamp.FromDateTimeOffset(time.GetUtcNow()) } },
            log, oddStreamIds: true);
        if (hello.KindCase == ConnectionHello.KindOneofCase.Enroll)
        {
            await conn.SendAsync(new AgentToControl { SetupStage = new SetupStage { Stage = "agent_installed", Ok = true } }, stop);
            await conn.SendAsync(new AgentToControl { SetupStage = new SetupStage { Stage = "connected", Ok = true } }, stop);
        }
        await conn.SendAsync(new AgentToControl { HostInfo = info }, stop);
        var run = conn.RunAsync(m => commands.HandleAsync(conn, m, stop), stop);
        await commands.OnConnectedAsync(conn, stop);
        using var metricsCts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        StartMetricsLoop(conn, metricsCts);
        await run;
        await metricsCts.CancelAsync();
        return state.Disconnected ? Outcome.Fatal : Outcome.Connected;
    }

    private async Task<Stream?> OpenStreamAsync(TcpClient tcp, AgentOptions o, string[] parts, CancellationToken stop)
    {
        try
        {
            await tcp.ConnectAsync(parts[0], parts.Length > 1 ? int.Parse(parts[1]) : 7443, stop);
        }
        catch (SocketException e)
        {
            log.LogWarning("Could not connect to {Cp}: {Msg}", o.ControlPlane, e.Message);
            return null;
        }
        Stream stream = tcp.GetStream();
        try { stream = await AgentTls.WrapAsync(stream, o, parts[0], stop); }
        catch (System.Security.Authentication.AuthenticationException e)
        {
            log.LogError("TLS to {Cp} failed (wrong certificate pin?): {Msg}", o.ControlPlane, e.Message);
            return null;
        }
        return stream;
    }

    private static ConnectionHello BuildHello(AgentOptions o, HostInfo info) =>
        o.Credential is { Length: > 0 } && o.EnrollToken is null
        ? new ConnectionHello { Register = new RegisterHello { Credential = o.Credential, AgentVersion = Version, HostInfo = info } }
        : new ConnectionHello { Enroll = new EnrollHello
            { EnrollToken = o.EnrollToken ?? "", AgentVersion = Version, HostInfo = info, PreviousCredential = o.Credential ?? "" } };

    private Outcome HandleRejection(AgentOptions o, HelloResult res)
    {
        log.LogError("Control plane rejected: {Reason}", res.ErrorReason);
        if (res.ErrorReason == "FLEET_ENROLL_TOKEN_INVALID" && o.Credential is { Length: > 0 } && o.EnrollToken is not null)
        {
            o.EnrollToken = null;
            state.Save();
            return Outcome.Failed;
        }
        return res.ErrorReason is "FLEET_ENROLL_TOKEN_INVALID" or "FLEET_CREDENTIAL_INVALID" ? Outcome.Fatal : Outcome.Failed;
    }

    private void AcceptHello(AgentOptions o, ConnectionHello hello, HelloResult res)
    {
        if (hello.KindCase == ConnectionHello.KindOneofCase.Enroll)
        {
            o.Credential = res.Credential;
            o.EnrollToken = null;
            o.HostId = res.HostId;
            o.OverlayIp = res.OverlayIp;
            state.Save();
            log.LogInformation("Enrolled: host {Host}, overlay {Ip}", res.HostId, res.OverlayIp);
        }
        else log.LogInformation("Connected: host {Host}", res.HostId);
    }

    private void StartMetricsLoop(Connection<ControlToAgent, AgentToControl> conn, CancellationTokenSource metricsCts)
    {
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), time);
            do
            {
                try { await conn.SendAsync(new AgentToControl { HostMetrics = await MetricsSampler.SampleAsync(state.Options, metricsCts.Token) }, metricsCts.Token); }
                catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException) { return; }
            } while (await timer.WaitForNextTickAsync(metricsCts.Token).AsTask().ContinueWith(t => !t.IsCanceled && t.Result, TaskScheduler.Default));
        }, CancellationToken.None);
    }
}
