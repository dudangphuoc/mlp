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

[RequiresPermission("run.read")]
public sealed record GetPromptQuery(Guid WorkspaceId, string Key) : IQuery<(PromptRow Prompt, IReadOnlyList<PromptVersionRow> Versions)>;

public sealed class GetPromptQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetPromptQuery, (PromptRow Prompt, IReadOnlyList<PromptVersionRow> Versions)>
{
    public async ValueTask<(PromptRow Prompt, IReadOnlyList<PromptVersionRow> Versions)> Handle(GetPromptQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var p = await db.GetPromptAsync(q.WorkspaceId, q.Key, ct) ?? throw new NotFoundException("LLM_PROMPT_NOT_FOUND", "Prompt not found");
        var vs = await db.ListPromptVersionsAsync(p.Id, ct);
        return (p with { LatestVersion = vs.Count > 0 ? vs[0].Version : 0 }, vs);
    }
}
