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

/// <summary>Runs a prompt version on an LLM deployment (the version's default, or an override) — used for quick tests and version comparison.</summary>
public sealed record RunPromptCommand(Guid WorkspaceId, string PromptKey, int Version, IReadOnlyDictionary<string, string> Variables, Guid? DeploymentId)
    : ICommand<(PromptVersionRow Version, LlmCallResult Result)>;

public sealed class RunPromptCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, ILlmInvoker invoker)
    : ICommandHandler<RunPromptCommand, (PromptVersionRow Version, LlmCallResult Result)>
{
    public async ValueTask<(PromptVersionRow Version, LlmCallResult Result)> Handle(RunPromptCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        var (p, v) = await PromptLookup.ResolveAsync(db, c.WorkspaceId, c.PromptKey, c.Version, ct);
        var dep = await PromptLookup.DeploymentAsync(db, c.WorkspaceId, c.DeploymentId ?? v.DeploymentId, ct);
        var r = await LlmCall.RunAsync(db, clock, invoker, dep, PromptLookup.Messages(v, c.Variables), v.MaxTokens, v.Temperature, "prompt", p.Id, v.Version, ct);
        return (v, r);
    }
}
