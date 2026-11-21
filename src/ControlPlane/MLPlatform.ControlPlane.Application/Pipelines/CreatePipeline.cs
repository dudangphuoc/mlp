using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Pipelines;

// ───────────── RPC ─────────────

public sealed record CreatePipelineCommand(Guid WorkspaceId, string Name, string DefinitionJson, string TriggerJson) : ICommand<PipelineRow>;

public sealed class CreatePipelineCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreatePipelineCommand, PipelineRow>
{
    public async ValueTask<PipelineRow> Handle(CreatePipelineCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        if (string.IsNullOrWhiteSpace(c.Name)) throw new ValidationException(new Dictionary<string, string> { ["name"] = "Pipeline name is required" });
        _ = PipelineDef.Parse(c.DefinitionJson);
        var trigger = ParseTrigger(c.TriggerJson);
        var id = Ids.New();
        try { await db.InsertPipelineAsync(id, c.WorkspaceId, c.Name.Trim(), c.DefinitionJson, trigger.ToJsonString(), uid, clock.UtcNow, ct); }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505") { throw new ConflictException("PIPELINE_NAME_TAKEN", "Pipeline name is already in use"); }
        return (await db.GetPipelineAsync(id, c.WorkspaceId, ct))!;
    }

    /// <summary>manual (default) | interval {minutes ≥ 5} | event {event: deployment.drift | …}.</summary>
    private static JsonObject ParseTrigger(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject { ["type"] = "manual" };
        JsonObject t;
        try { t = JsonNode.Parse(json) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new ValidationException(new Dictionary<string, string> { ["trigger"] = "Invalid JSON" }); }
        var ok = t["type"]?.ToString() switch
        {
            "manual" => true,
            "interval" => t["minutes"] is JsonValue m && m.TryGetValue<int>(out var mins) && mins >= 5,
            "event" => t["event"]?.ToString() is "deployment.drift",
            _ => false,
        };
        if (!ok) throw new InvalidArgumentException("PIPELINE_TRIGGER_INVALID", "Trigger: manual | interval {minutes ≥ 5} | event {event: deployment.drift}");
        return t;
    }
}
