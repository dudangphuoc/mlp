using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/// <summary>Declare (create) or update a model on a connection.</summary>
[Access(AccessKind.InstanceAdmin)]
public sealed record SaveAiModelCommand(Guid? Id, Guid ConnectionId, string ModelId, string DisplayName, string[] Roles, string[] Inputs, string[] Outputs,
    string[] Skills, string Tier, double? InputUsdPer1m, double? OutputUsdPer1m, int MaxTokens, bool IsActive) : ICommand<AiModelRow>;

public sealed class SaveAiModelCommandHandler(IWriteSession db, IClock clock, InstanceAdminGuard guard) : ICommandHandler<SaveAiModelCommand, AiModelRow>
{
    public async ValueTask<AiModelRow> Handle(SaveAiModelCommand c, CancellationToken ct)
    {
        var uid = await guard.RequireAsync(ct);
        var conn = await db.GetAiConnectionAsync(c.ConnectionId, ct) ?? throw new NotFoundException("AI_CONNECTION_NOT_FOUND", "Connection not found");
        var roles = AiCatalog.Clean(c.Roles, AiCatalog.Roles, "roles");
        if (roles.Length == 0) throw new ValidationException(new Dictionary<string, string> { ["roles"] = "Pick at least one role" });
        if (AiCatalog.IsDecisionServer(conn.ProviderType) && roles.Any(r => r is not ("decision" or "judge")))
            throw new ValidationException(new Dictionary<string, string> { ["roles"] = "A decision server can only serve the decision and judge roles" });
        var inputs = AiCatalog.Clean(c.Inputs.Length == 0 ? ["text"] : c.Inputs, AiCatalog.Inputs, "inputs");
        var outputs = AiCatalog.Clean(c.Outputs.Length == 0 ? ["text"] : c.Outputs, AiCatalog.Outputs, "outputs");
        var skills = AiCatalog.Clean(c.Skills, AiCatalog.Skills, "skills");
        var tier = AiCatalog.Clean([string.IsNullOrWhiteSpace(c.Tier) ? "standard" : c.Tier], AiCatalog.Tiers, "tier")[0];
        if (string.IsNullOrWhiteSpace(c.ModelId)) throw new ValidationException(new Dictionary<string, string> { ["model_id"] = "Model name is required" });
        var maxTokens = Math.Clamp(c.MaxTokens <= 0 ? 4096 : c.MaxTokens, 16, 2_000_000);
        Guid id;
        AiModelRow? before = null;
        if (c.Id is { } existing)
        {
            before = await db.GetAiModelAsync(existing, ct) ?? throw new NotFoundException("AI_MODEL_NOT_FOUND", "Model not found");
            id = existing;
            await db.UpdateAiModelAsync(id, c.DisplayName.Trim(), roles, inputs, outputs, skills, tier, c.InputUsdPer1m, c.OutputUsdPer1m, maxTokens, c.IsActive, clock.UtcNow, ct);
        }
        else
        {
            id = Ids.New();
            try
            {
                await db.InsertAiModelAsync(id, conn.Id, c.ModelId.Trim(), c.DisplayName.Trim(), roles, inputs, outputs, skills, tier, c.InputUsdPer1m, c.OutputUsdPer1m,
                    maxTokens, clock.UtcNow, ct);
            }
            catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("AI_MODEL_EXISTS", "This model is already declared on the connection"); }
        }
        var row = (await db.GetAiModelAsync(id, ct))!;
        await AiAudit.WriteAsync(db, clock, uid, before is null ? "model.create" : "model.update", $"{conn.Name} / {row.ModelId}",
            before is null ? null : AiAudit.Of(before), AiAudit.Of(row), ct);
        return row;
    }
}
