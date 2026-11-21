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

[RequiresPermission("model.write")]
public sealed record CreatePromptVersionCommand(Guid WorkspaceId, string PromptKey, string System, string Template, Guid? DeploymentId, double Temperature,
    int MaxTokens, string Note) : ICommand<PromptVersionRow>;

public sealed class CreatePromptVersionCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreatePromptVersionCommand, PromptVersionRow>
{
    public async ValueTask<PromptVersionRow> Handle(CreatePromptVersionCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var p = await db.GetPromptAsync(c.WorkspaceId, c.PromptKey, ct) ?? throw new NotFoundException("LLM_PROMPT_NOT_FOUND", "Prompt not found");
        if (string.IsNullOrWhiteSpace(c.Template)) throw new ValidationException(new Dictionary<string, string> { ["template"] = "Template is empty" });
        if (c.DeploymentId is { } d) _ = await db.GetDeploymentAsync(d, c.WorkspaceId, ct) ?? throw new NotFoundException("SERVE_NOT_FOUND", "Deployment not found");
        var vars = PromptTemplate.Variables(c.System ?? "", c.Template);
        var v = await db.InsertPromptVersionAsync(p.Id, c.System ?? "", c.Template, new JsonArray(vars.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()).ToJsonString(),
            c.DeploymentId, c.Temperature is >= 0 and <= 2 ? c.Temperature : 0.7, c.MaxTokens is > 0 and <= 32768 ? c.MaxTokens : 512, c.Note ?? "", uid, clock.UtcNow, ct);
        return (await db.ListPromptVersionsAsync(p.Id, ct)).First(x => x.Version == v);
    }
}
