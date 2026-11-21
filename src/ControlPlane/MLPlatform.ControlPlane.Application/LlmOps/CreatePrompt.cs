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

// ───────────── Prompt registry ─────────────

public sealed record CreatePromptCommand(Guid WorkspaceId, string Name, string Description) : ICommand<PromptRow>;

public sealed class CreatePromptCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreatePromptCommand, PromptRow>
{
    public async ValueTask<PromptRow> Handle(CreatePromptCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        if (!Identity.IdentityRules.Slug().IsMatch(c.Name)) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Name may only contain lowercase letters, numbers and hyphens" });
        var id = Ids.New();
        try { await db.InsertPromptAsync(id, c.WorkspaceId, c.Name, c.Description, uid, clock.UtcNow, ct); }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("LLM_PROMPT_NAME_TAKEN", "Prompt name is already in use"); }
        return (await db.GetPromptAsync(c.WorkspaceId, id.ToString(), ct))!;
    }
}
