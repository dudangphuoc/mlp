using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/// <summary>Update a connection. SecretEnc null = keep the stored key; ClearKey removes it.</summary>
public sealed record UpdateAiConnectionCommand(Guid Id, string Name, string Endpoint, string? SecretEnc, bool ClearKey, int MaxConcurrency, int RpmLimit,
    int TimeoutMs, bool IsActive, bool IsExternal) : ICommand<AiConnectionRow>;

public sealed class UpdateAiConnectionCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<UpdateAiConnectionCommand, AiConnectionRow>
{
    public async ValueTask<AiConnectionRow> Handle(UpdateAiConnectionCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        var old = await db.GetAiConnectionAsync(c.Id, ct) ?? throw new NotFoundException("AI_CONNECTION_NOT_FOUND", "Connection not found");
        if (!Uri.TryCreate(c.Endpoint.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
            throw new ValidationException(new Dictionary<string, string> { ["endpoint"] = "Enter an http(s) URL" });
        var secret = c.ClearKey ? null : c.SecretEnc ?? old.SecretEnc;
        await db.UpdateAiConnectionAsync(c.Id, string.IsNullOrWhiteSpace(c.Name) ? old.Name : c.Name.Trim(), c.Endpoint.Trim().TrimEnd('/'), secret,
            Math.Clamp(c.MaxConcurrency, 1, 256), Math.Max(0, c.RpmLimit), Math.Clamp(c.TimeoutMs, 500, 600_000), c.IsActive, c.IsExternal, clock.UtcNow, ct);
        var row = (await db.GetAiConnectionAsync(c.Id, ct))!;
        await AiAudit.WriteAsync(db, clock, uid, "connection.update", row.Name, AiAudit.Of(old), AiAudit.Of(row), ct);
        return row;
    }
}
