using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

internal static class AiAudit
{
    public static Task WriteAsync(IWriteSession db, IClock clock, Guid actor, string action, string target, object? before, object? after, CancellationToken ct) =>
        db.InsertAiAuditAsync(Ids.New(), actor, action, target, Json(before), Json(after), clock.UtcNow, ct);

    private static string? Json(object? o) => o switch
    {
        null => null,
        JsonNode n => n.ToJsonString(),
        string s => new JsonObject { ["value"] = s }.ToJsonString(),
        _ => new JsonObject { ["value"] = o.ToString() }.ToJsonString(),
    };

    public static JsonObject Of(AiConnectionRow c) => new()
    {
        ["name"] = c.Name, ["type"] = c.ProviderType, ["endpoint"] = c.Endpoint, ["key"] = c.SecretEnc is null ? "none" : "set",
        ["max_concurrency"] = c.MaxConcurrency, ["rpm_limit"] = c.RpmLimit, ["timeout_ms"] = c.TimeoutMs, ["active"] = c.IsActive, ["external"] = c.IsExternal,
    };

    public static JsonObject Of(AiModelRow m) => new()
    {
        ["model"] = m.ModelId, ["roles"] = string.Join(",", m.Roles), ["inputs"] = string.Join(",", m.Inputs), ["outputs"] = string.Join(",", m.Outputs),
        ["skills"] = string.Join(",", m.Skills), ["tier"] = m.Tier, ["max_tokens"] = m.MaxTokens, ["active"] = m.IsActive,
        ["price_in"] = m.InputUsdPer1m, ["price_out"] = m.OutputUsdPer1m,
    };
}
