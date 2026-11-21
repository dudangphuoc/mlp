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

// ───────────── Guardrails + pricing per deployment ─────────────

[RequiresPermission("run.read")]
public sealed record GetLlmSettingsQuery(Guid WorkspaceId, Guid DeploymentId) : IQuery<LlmSettingsRow>;

public sealed class GetLlmSettingsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetLlmSettingsQuery, LlmSettingsRow>
{
    public async ValueTask<LlmSettingsRow> Handle(GetLlmSettingsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        _ = await db.GetDeploymentAsync(q.DeploymentId, q.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        return await db.GetLlmSettingsAsync(q.DeploymentId, ct) ?? new LlmSettingsRow(q.DeploymentId, "off", false, "[]", false, 0, DateTimeOffset.MinValue);
    }
}
