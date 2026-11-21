using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.LlmOps;

public sealed record UpdateLlmSettingsCommand(Guid WorkspaceId, Guid DeploymentId, string PiiMode, bool BlockInjection, IReadOnlyList<string> BlockedTerms,
    bool CheckOutput, double PricePer1k) : ICommand<LlmSettingsRow>;

public sealed class UpdateLlmSettingsCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<UpdateLlmSettingsCommand, LlmSettingsRow>
{
    public async ValueTask<LlmSettingsRow> Handle(UpdateLlmSettingsCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        _ = await db.GetDeploymentAsync(c.DeploymentId, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        var mode = string.IsNullOrEmpty(c.PiiMode) ? "off" : c.PiiMode;
        if (mode is not ("off" or "redact" or "block")) throw new ValidationException(new Dictionary<string, string> { ["pii_mode"] = "off | redact | block" });
        var terms = new JsonArray(c.BlockedTerms.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().Take(500).Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());
        await db.UpsertLlmSettingsAsync(c.DeploymentId, c.WorkspaceId, mode, c.BlockInjection, terms.ToJsonString(), c.CheckOutput, Math.Max(0, c.PricePer1k), uid, clock.UtcNow, ct);
        return (await db.GetLlmSettingsAsync(c.DeploymentId, ct))!;
    }
}
