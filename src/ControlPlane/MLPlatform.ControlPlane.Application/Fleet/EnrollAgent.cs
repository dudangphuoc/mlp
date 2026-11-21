using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

[Access(AccessKind.WorkloadToken)]
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
