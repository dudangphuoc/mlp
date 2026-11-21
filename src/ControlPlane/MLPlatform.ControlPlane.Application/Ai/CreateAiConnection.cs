using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/// <summary>Create a connection (the api encrypted the key).</summary>
[Access(AccessKind.InstanceAdmin)]
public sealed record CreateAiConnectionCommand(string Name, string ProviderType, string Endpoint, string? SecretEnc, int MaxConcurrency, int RpmLimit, int TimeoutMs,
    bool? IsExternal = null)
    : ICommand<AiConnectionRow>;

public sealed class CreateAiConnectionCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<CreateAiConnectionCommand, AiConnectionRow>
{
    public async ValueTask<AiConnectionRow> Handle(CreateAiConnectionCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        if (string.IsNullOrWhiteSpace(c.Name)) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Name is required" });
        if (!AiCatalog.ProviderTypes.Contains(c.ProviderType)) throw new ValidationException(new Dictionary<string, string> { ["provider_type"] = "Unknown provider type" });
        if (!Uri.TryCreate(c.Endpoint.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
            throw new ValidationException(new Dictionary<string, string> { ["endpoint"] = "Enter an http(s) URL" });
        var id = Ids.New();
        try
        {
            await db.InsertAiConnectionAsync(id, c.Name.Trim(), c.ProviderType, c.Endpoint.Trim().TrimEnd('/'), c.SecretEnc,
                Math.Clamp(c.MaxConcurrency, 1, 256), Math.Max(0, c.RpmLimit), Math.Clamp(c.TimeoutMs, 500, 600_000),
                c.IsExternal ?? AiCatalog.ExternalByDefault(c.ProviderType), uid, clock.UtcNow, ct);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("AI_CONNECTION_EXISTS", "A connection with this name already exists"); }
        var row = (await db.GetAiConnectionAsync(id, ct))!;
        await AiAudit.WriteAsync(db, clock, uid, "connection.create", row.Name, null, AiAudit.Of(row), ct);
        return row;
    }
}
