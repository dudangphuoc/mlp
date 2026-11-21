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

// ───────────── Eval ─────────────

/// <summary>Eval set: `[{"vars":{…}, "expect":{"contains","not_contains","equals","regex","max_chars"}}]` (≤ 100 cases).</summary>
public sealed record CreateEvalSetCommand(Guid WorkspaceId, string Name, string CasesJson) : ICommand<EvalSetRow>;

public sealed class CreateEvalSetCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreateEvalSetCommand, EvalSetRow>
{
    public async ValueTask<EvalSetRow> Handle(CreateEvalSetCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        JsonArray cases;
        try { cases = JsonNode.Parse(c.CasesJson) as JsonArray ?? throw new JsonException(); }
        catch (JsonException) { throw new ValidationException(new Dictionary<string, string> { ["cases"] = "Expected a JSON array of cases" }); }
        if (cases.Count is 0 or > 100) throw new ValidationException(new Dictionary<string, string> { ["cases"] = "1–100 case" });
        foreach (var (node, i) in cases.Select((n, i) => (n, i)))
        {
            if (node is not JsonObject o || o["vars"] is not JsonObject || o["expect"] is not JsonObject e || e.Count == 0)
                throw new ValidationException(new Dictionary<string, string> { ["cases"] = $"Case {i}: requires vars (object) and expect (object, ≥ 1 condition)" });
            if (e["regex"]?.ToString() is { } rx)
                try { _ = new Regex(rx, RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
                catch (ArgumentException) { throw new ValidationException(new Dictionary<string, string> { ["cases"] = $"Case {i}: invalid regex" }); }
        }
        var id = Ids.New();
        try { await db.InsertEvalSetAsync(id, c.WorkspaceId, c.Name.Trim(), c.CasesJson, uid, clock.UtcNow, ct); }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("LLM_EVAL_SET_NAME_TAKEN", "Eval set name is already in use"); }
        return (await db.GetEvalSetAsync(id, c.WorkspaceId, ct))!;
    }
}
