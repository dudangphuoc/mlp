using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record SaveAssistSettingsCommand(Guid WorkspaceId, Guid ProjectId, string SourcesJson, string ClassMappingJson, bool AllowExternal, double MinConfidence)
    : ICommand<ProjectAssistSettings>;

public sealed class SaveAssistSettingsCommandHandler(IWriteSession db, LabelAccess access, WorkspaceAuthorizer auth, IClock clock)
    : ICommandHandler<SaveAssistSettingsCommand, ProjectAssistSettings>
{
    public async ValueTask<ProjectAssistSettings> Handle(SaveAssistSettingsCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await AssistAccess.ManageAsync(access, auth, c.WorkspaceId, c.ProjectId, ct);
        if (c.MinConfidence is < 0 or > 1) throw LabelRules.Invalid("min_confidence", "Minimum confidence must be between 0 and 1");
        // Normalize: only known kinds, valid ids, no duplicates; mapping values must be schema class ids.
        var sources = new JsonArray();
        var seen = new HashSet<string>();
        try
        {
            foreach (var n in JsonNode.Parse(string.IsNullOrEmpty(c.SourcesJson) ? "[]" : c.SourcesJson) as JsonArray ?? [])
            {
                var k = n?["kind"]?.ToString();
                if (k is not ("model" or "deployment") || !Guid.TryParse(n!["id"]?.ToString(), out var id)) throw LabelRules.Invalid("sources", "Each source needs kind model|deployment and an id");
                if (seen.Add($"{k}:{id}")) sources.Add((JsonNode)new JsonObject { ["kind"] = k, ["id"] = id.ToString() });
            }
        }
        catch (System.Text.Json.JsonException) { throw LabelRules.Invalid("sources", "Invalid JSON"); }
        var schema = await db.GetSchemaVersionAsync(p.DatasetId, p.SchemaVersion, ct);
        var ids = LabelRules.ParseClasses(schema?.Classes ?? "[]");
        var map = new JsonObject();
        try
        {
            if (JsonNode.Parse(string.IsNullOrEmpty(c.ClassMappingJson) ? "{}" : c.ClassMappingJson) is JsonObject o)
                foreach (var (k, v) in o)
                {
                    if (string.IsNullOrWhiteSpace(k)) continue;
                    if (v is not JsonValue jv || !jv.TryGetValue<int>(out var cid) || !ids.Contains(cid)) throw LabelRules.Invalid("class_mapping", $"\"{k}\" maps to a class that is not in the schema");
                    map[k.Trim()] = cid;
                }
        }
        catch (System.Text.Json.JsonException) { throw LabelRules.Invalid("class_mapping", "Invalid JSON"); }
        await db.UpsertAssistSettingsAsync(p.Id, p.WorkspaceId, sources.ToJsonString(), map.ToJsonString(), c.AllowExternal, Math.Round(c.MinConfidence, 3), uid, clock.UtcNow, ct);
        return ProjectAssistSettings.From(await db.GetAssistSettingsAsync(p.Id, p.WorkspaceId, ct));
    }
}
