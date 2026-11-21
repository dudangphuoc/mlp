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

/// <summary>A connected agent.</summary>
public sealed class AgentSession(Guid hostId, Guid workspaceId, Connection<AgentToControl, ControlToAgent> conn)
{
    public Guid HostId { get; } = hostId;
    public Guid WorkspaceId { get; } = workspaceId;
    public Connection<AgentToControl, ControlToAgent> Conn { get; } = conn;
    public PendingCommands Pending { get; } = new();
    /// <summary>HostInfo at hello (platform, version, install location, service manager; used by OTA).</summary>
    public HostInfo? Info { get; init; }
    public string AgentVersion { get; init; } = "";
    public DateTimeOffset ConnectedAt { get; init; }
    /// <summary>Hello + overlay IP (rebuilds the network/Ray node when Docker on the host comes back).</summary>
    public ConnectionHello? Hello { get; init; }
    public string OverlayIp { get; init; } = "";

    /// <summary>Recent commands sent to this agent, newest last — shown on the machine so nothing happens on it unseen.</summary>
    public sealed class CommandTrace(string id, string kind, string detail, DateTimeOffset started, TimeSpan limit)
    {
        public string Id { get; } = id;
        public string Kind { get; } = kind;
        public string Detail { get; } = detail;
        public DateTimeOffset Started { get; } = started;
        public TimeSpan Limit { get; } = limit;
        public DateTimeOffset? Finished { get; set; }
        public string State { get; set; } = "running";   // running | ok | failed | timeout | lost
        public string Message { get; set; } = "";
    }
    private readonly Queue<CommandTrace> _trace = new();
    public IReadOnlyList<CommandTrace> Trace { get { lock (_trace) return [.. _trace]; } }

    private static string Describe(AgentCommand c) => c.KindCase switch
    {
        AgentCommand.KindOneofCase.ImageBuild => $"build {c.ImageBuild.Tag} ({c.ImageBuild.Files.Count} files)",
        AgentCommand.KindOneofCase.ContainerApply => $"run {c.ContainerApply.Spec.Name} · {c.ContainerApply.Spec.Image}",
        AgentCommand.KindOneofCase.ContainerStop => $"stop {c.ContainerStop.Label}",
        AgentCommand.KindOneofCase.ContainerRemove => $"remove {c.ContainerRemove.Label}",
        AgentCommand.KindOneofCase.ContainerRunOnce => $"run once {c.ContainerRunOnce.Spec.Image}",
        AgentCommand.KindOneofCase.PrepullImages => $"pull {string.Join(", ", c.PrepullImages.Images)}",
        AgentCommand.KindOneofCase.FileRead => $"read {c.FileRead.Path}",
        AgentCommand.KindOneofCase.FileWrite => $"write {c.FileWrite.Path}",
        AgentCommand.KindOneofCase.RunScript => $"script {c.RunScript.Path}",
        _ => c.KindCase.ToString(),
    };

    public async Task<CommandResult> SendCommandAsync(AgentCommand cmd, TimeSpan timeout, CancellationToken ct)
    {
        cmd.CommandId = string.IsNullOrEmpty(cmd.CommandId) ? Guid.NewGuid().ToString("N") : cmd.CommandId;
        var trace = cmd.KindCase == AgentCommand.KindOneofCase.FileRead && cmd.FileRead.Path == "agent.log" ? null
            : new CommandTrace(cmd.CommandId, cmd.KindCase.ToString(), Describe(cmd), TimeProvider.System.GetUtcNow(), timeout);
        if (trace is not null) lock (_trace) { _trace.Enqueue(trace); while (_trace.Count > 50) _trace.Dequeue(); }
        var wait = Pending.Register(cmd.CommandId, timeout);
        try
        {
            await Conn.SendAsync(new ControlToAgent { StreamId = Conn.NewStreamId(), Command = cmd }, ct);
            var r = await wait;
            if (trace is not null)
            {
                trace.State = r.Ok ? "ok" : r.ErrorReason == "FLEET_COMMAND_TIMEOUT" && string.IsNullOrEmpty(r.Message) ? "timeout" : "failed";
                trace.Message = r.Ok ? "" : trace.State == "timeout" ? $"No answer from the agent within {timeout.TotalMinutes:0} min" : $"{r.ErrorReason}: {r.Message}";
            }
            return r;
        }
        catch (Exception e)
        {
            if (trace is not null)
            {
                trace.State = e is TimeoutException ? "timeout" : "lost";
                trace.Message = e is TimeoutException ? $"No answer from the agent within {timeout.TotalMinutes:0} min" : e.Message;
            }
            throw;
        }
        finally { if (trace is not null) trace.Finished = TimeProvider.System.GetUtcNow(); }
    }
}
