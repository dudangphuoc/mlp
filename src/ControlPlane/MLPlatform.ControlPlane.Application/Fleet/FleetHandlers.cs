using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

public sealed record EnrollmentDto(Guid Id, string Token, DateTimeOffset ExpiresAt, Guid WorkspaceId);

// ---- Create the machine plug-in command (M02 §5.1) ----
public sealed record CreateEnrollmentCommand(Guid WorkspaceId, string Os, string DataPath, string ModelsPath) : ICommand<EnrollmentDto>;

public sealed class CreateEnrollmentCommandHandler(IWriteSession db, IClock clock, TokenService tokens, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateEnrollmentCommand, EnrollmentDto>
{
    public async ValueTask<EnrollmentDto> Handle(CreateEnrollmentCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "fleet.manage", ct);
        if (string.IsNullOrWhiteSpace(c.DataPath) || string.IsNullOrWhiteSpace(c.ModelsPath))
            throw new ValidationException(new Dictionary<string, string> { ["data_path"] = "data_path and models_path are required" });
        var token = TokenService.New("mlpe_");
        var now = clock.UtcNow;
        var id = Ids.New();
        await db.InsertEnrollmentAsync(id, c.WorkspaceId, tokens.Hash(token), c.Os, c.DataPath, c.ModelsPath, uid, now,
            now.AddMinutes(10), ct);
        return new EnrollmentDto(id, token, now.AddMinutes(10), c.WorkspaceId);
    }
}

// ---- Agent enroll over the :7443 channel (no user — authenticated by an mlpe_ token) ----
public sealed record AgentEnrollResult(Guid HostId, Guid WorkspaceId, string Credential, string OverlayIp);

public sealed record EnrollAgentCommand(string EnrollToken, string PreviousCredential, string MachineName,
    string AgentVersion, string? WgPublicKey, byte[] HostInfo) : ICommand<AgentEnrollResult>;

public sealed class EnrollAgentCommandHandler(IWriteSession db, IClock clock, TokenService tokens)
    : ICommandHandler<EnrollAgentCommand, AgentEnrollResult>
{
    public async ValueTask<AgentEnrollResult> Handle(EnrollAgentCommand c, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var e = await db.GetEnrollmentByTokenAsync(tokens.Hash(c.EnrollToken), ct);
        if (e is null || e.UsedAt is not null || e.ExpiresAt < now)
            throw new UnauthenticatedException("FLEET_ENROLL_TOKEN_INVALID", "Install command has expired or was already used");
        var credential = TokenService.New("mlpa_");
        var credHash = tokens.Hash(credential);

        // Reinstall on the same machine: keep host_id + overlay_ip (20/04 §2 step 6)
        if (!string.IsNullOrEmpty(c.PreviousCredential)
            && await db.GetHostByCredentialAsync(tokens.Hash(c.PreviousCredential), ct) is { } old
            && old.WorkspaceId == e.WorkspaceId)
        {
            await db.ReEnrollHostAsync(old.Id, credHash, c.WgPublicKey, c.AgentVersion, c.HostInfo, e.DataPath, e.ModelsPath, now, ct);
            await db.UseEnrollmentAsync(e.Id, old.Id, now, ct);
            return new AgentEnrollResult(old.Id, e.WorkspaceId, credential, old.OverlayIp.Split('/')[0]);
        }

        var seq = await db.NextOverlaySeqAsync(ct);
        if (seq > 65_000) throw new ResourceExhaustedException("FLEET_OVERLAY_EXHAUSTED", "Overlay addresses exhausted");
        var ip = $"10.88.{seq / 254}.{seq % 254 + 1}";
        var hostId = Ids.New();
        await db.InsertHostAsync(hostId, e.WorkspaceId, c.MachineName, ip, c.WgPublicKey, credHash, c.AgentVersion,
            e.DataPath, e.ModelsPath, c.HostInfo, now, ct);
        await db.UseEnrollmentAsync(e.Id, hostId, now, ct);
        return new AgentEnrollResult(hostId, e.WorkspaceId, credential, ip);
    }
}

// ---- Agent register (reconnect) ----
public sealed record AgentRegisterCommand(string Credential, string AgentVersion, byte[]? HostInfo, string? WgPublicKey = null) : ICommand<HostRow>;

public sealed class AgentRegisterCommandHandler(IWriteSession db, IClock clock, TokenService tokens)
    : ICommandHandler<AgentRegisterCommand, HostRow>
{
    public async ValueTask<HostRow> Handle(AgentRegisterCommand c, CancellationToken ct)
    {
        var h = await db.GetHostByCredentialAsync(tokens.Hash(c.Credential), ct);
        if (h is null || h.DisconnectedAt is not null)
            throw new UnauthenticatedException("FLEET_CREDENTIAL_INVALID", "Invalid machine credential");
        await db.SetHostOnlineAsync(h.Id, "online", c.AgentVersion, c.HostInfo, clock.UtcNow, ct);
        if (!string.IsNullOrEmpty(c.WgPublicKey)) await db.SetHostWgKeyAsync(h.Id, c.WgPublicKey, ct);
        return h with { State = "online" };
    }
}

public sealed record AgentHeartbeatCommand(Guid HostId, byte[]? HostInfo, string AgentVersion) : ICommand<bool>;

public sealed class AgentHeartbeatCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<AgentHeartbeatCommand, bool>
{
    public async ValueTask<bool> Handle(AgentHeartbeatCommand c, CancellationToken ct)
    {
        await db.SetHostOnlineAsync(c.HostId, "online", c.AgentVersion, c.HostInfo, clock.UtcNow, ct);
        return true;
    }
}

public sealed record MarkHostOfflineCommand(Guid HostId, string Reason) : ICommand<bool>;

public sealed class MarkHostOfflineCommandHandler(IWriteSession db, IClock clock, MLPlatform.ControlPlane.Application.Ops.RecoveryOptions recovery) : ICommandHandler<MarkHostOfflineCommand, bool>
{
    public async ValueTask<bool> Handle(MarkHostOfflineCommand c, CancellationToken ct)
    {
        var before = await MLPlatform.ControlPlane.Application.Notifications.NotifySql.HostInfoForNotifyAsync(db, c.HostId, ct);
        await db.SetHostStateAsync(c.HostId, "offline", c.Reason, clock.UtcNow, ct);
        if (before is not null)
            await MLPlatform.ControlPlane.Application.Ops.Suspects.MarkAsync(db, clock, recovery, c.HostId, before.Name, ct);
        if (before is { State: not "offline" })
            await MLPlatform.ControlPlane.Application.Notifications.Notifier.SendAsync(db, clock, before.WorkspaceId,
                await MLPlatform.ControlPlane.Application.Notifications.Notifier.AdminsAsync(db, before.WorkspaceId, ct), "fleet.host_offline",
                $"Machine {before.Name} is offline", "#/fleet", $"host/{before.Id}", "warning", ct);
        return true;
    }
}

// ---- Queries for the UI ----
public sealed record ListHostsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<HostRow>>;

public sealed class ListHostsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListHostsQuery, IReadOnlyList<HostRow>>
{
    public async ValueTask<IReadOnlyList<HostRow>> Handle(ListHostsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        return await db.ListHostsAsync(q.WorkspaceId, ct);
    }
}

public sealed record GetHostQuery(Guid WorkspaceId, Guid HostId) : IQuery<HostRow>;

public sealed class GetHostQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetHostQuery, HostRow>
{
    public async ValueTask<HostRow> Handle(GetHostQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        return await db.GetHostAsync(q.HostId, q.WorkspaceId, ct)
               ?? throw new NotFoundException("FLEET_HOST_NOT_FOUND", "Machine not found");
    }
}

public sealed record DisconnectHostCommand(Guid WorkspaceId, Guid HostId) : ICommand<bool>;

public sealed class DisconnectHostCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<DisconnectHostCommand, bool>
{
    public async ValueTask<bool> Handle(DisconnectHostCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "fleet.manage", ct);
        await db.DisconnectHostAsync(c.HostId, c.WorkspaceId, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>Internal (scheduler): online machines of the workspace, no user permission check.</summary>
public sealed record ListOnlineHostsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<HostRow>>;

public sealed class ListOnlineHostsQueryHandler(IReadSession db) : IQueryHandler<ListOnlineHostsQuery, IReadOnlyList<HostRow>>
{
    public async ValueTask<IReadOnlyList<HostRow>> Handle(ListOnlineHostsQuery q, CancellationToken ct) => await db.ListOnlineHostsAsync(q.WorkspaceId, ct);
}

/// <summary>Internal (catalog environments, after the RPC checked the permission): every connected-or-not machine of the workspace that was not removed.</summary>
public sealed record ListWorkspaceHostsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<HostRow>>;

/// <summary>Same list as the Machines page, without the fleet permission.</summary>
public sealed class ListWorkspaceHostsQueryHandler(IReadSession db) : IQueryHandler<ListWorkspaceHostsQuery, IReadOnlyList<HostRow>>
{
    public async ValueTask<IReadOnlyList<HostRow>> Handle(ListWorkspaceHostsQuery q, CancellationToken ct) => await db.ListHostsAsync(q.WorkspaceId, ct);
}
