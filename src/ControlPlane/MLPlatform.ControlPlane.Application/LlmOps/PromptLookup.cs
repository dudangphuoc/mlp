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

internal static class PromptLookup
{
    public static async Task<(PromptRow, PromptVersionRow)> ResolveAsync(IReadSession db, Guid ws, string key, int version, CancellationToken ct)
    {
        var p = await db.GetPromptAsync(ws, key, ct) ?? throw new NotFoundException("LLM_PROMPT_NOT_FOUND", "Prompt not found");
        var vs = await db.ListPromptVersionsAsync(p.Id, ct);
        var v = (version > 0 ? vs.FirstOrDefault(x => x.Version == version) : vs.FirstOrDefault())
                ?? throw new NotFoundException("LLM_PROMPT_VERSION_NOT_FOUND", "Prompt has no such version");
        return (p, v);
    }

    public static async Task<DeploymentRow> DeploymentAsync(IReadSession db, Guid ws, Guid? id, CancellationToken ct) =>
        id is { } d ? await db.GetDeploymentAsync(d, ws, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found")
                    : throw new InvalidArgumentException("LLM_NO_DEPLOYMENT", "Prompt version has no deployment — choose an LLM deployment");

    public static List<(string, string)> Messages(PromptVersionRow v, IReadOnlyDictionary<string, string> vars)
    {
        var m = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(v.System)) m.Add(("system", PromptTemplate.Render(v.System, vars)));
        m.Add(("user", PromptTemplate.Render(v.Template, vars)));
        return m;
    }
}
