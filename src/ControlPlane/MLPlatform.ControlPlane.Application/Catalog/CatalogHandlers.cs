using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

/// <summary>
/// Syncs builtin recipes + the task catalog from the release's `recipes/` directory (M17 §2, D-30).
/// Each manifest goes through selfcheck (M17 R2a). State: `unbuilt` (selfcheck passed, awaiting CI build + smoke) or `invalid`.
/// </summary>
public sealed record SyncBuiltinRecipesCommand(string RecipesDir) : ICommand<int>;

public sealed class SyncBuiltinRecipesCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SyncBuiltinRecipesCommand, int>
{
    public async ValueTask<int> Handle(SyncBuiltinRecipesCommand c, CancellationToken ct)
    {
        var tasksDoc = YamlJson.Parse(await File.ReadAllTextAsync(Path.Combine(c.RecipesDir, "_tasks", "tasks.yaml"), ct))!;
        var known = new HashSet<string>();
        foreach (var t in tasksDoc["tasks"]!.AsArray().OfType<JsonObject>())
        {
            var key = Str(t, "key");
            known.Add(key);
            await db.UpsertTaskAsync(key, Str(t["modality"], "in"), Str(t["modality"], "out"), Arr(t["converters"]),
                Str(t, "primaryMetric"), Arr(t["metrics"]), ct);
        }
        var count = 0;
        foreach (var f in Directory.GetFiles(c.RecipesDir, "mlp-recipe.yaml", SearchOption.AllDirectories))
        {
            var text = await File.ReadAllTextAsync(f, ct);
            if (YamlJson.Parse(text) is not JsonObject m) continue;
            var errs = RecipeChecker.Check(m, known);
            var meta = m["metadata"] as JsonObject ?? [];
            var lic = m["license"] as JsonObject ?? [];
            var env = m["environment"] as JsonObject ?? [];
            var src = m["source"] as JsonObject ?? [];
            var source = src["git"] is not null ? $"{Str(src, "git")}@{Str(src, "commit")[..Math.Min(7, Str(src, "commit").Length)]}"
                : src["pip"] is JsonArray pip ? string.Join(" ", pip.Select(x => x!.ToString()))
                : $"{Str(src, "hf")}@{Str(src, "revision")}";
            await db.UpsertBuiltinRecipeAsync(Ids.New(), Str(meta, "name"), Str(meta, "displayName"), Str(meta, "family"),
                Str(meta, "description").Trim(), meta["maturity"]?.ToString() ?? "stable", Arr(m["tasks"]), Str(lic, "code"),
                Str(lic, "weights"), Str(lic, "notes").Trim(),
                lic["requiresAck"]?.GetValue<bool>() == true || Str(lic, "notes").Trim().Length > 0,
                env["gpu"]?.ToString() ?? "optional", env["minVramGb"] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0,
                source, m.ToJsonString(), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))),
                errs.Count == 0 ? "unbuilt" : "invalid", [.. errs], clock.UtcNow, ct);
            count++;
        }
        return count;
    }

    private static string Str(JsonNode? n, string k) => n?[k]?.ToString() ?? "";
    private static string[] Arr(JsonNode? n) => (n as JsonArray)?.Select(x => x!.ToString()).ToArray() ?? [];
}

public sealed record ListTasksQuery : IQuery<IReadOnlyList<TaskRow>>;

public sealed class ListTasksQueryHandler(IReadSession db) : IQueryHandler<ListTasksQuery, IReadOnlyList<TaskRow>>
{
    public async ValueTask<IReadOnlyList<TaskRow>> Handle(ListTasksQuery q, CancellationToken ct) => await db.ListTasksAsync(ct);
}

public sealed record ListRecipesQuery(Guid WorkspaceId, string Task) : IQuery<IReadOnlyList<RecipeRow>>;

public sealed class ListRecipesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListRecipesQuery, IReadOnlyList<RecipeRow>>
{
    public async ValueTask<IReadOnlyList<RecipeRow>> Handle(ListRecipesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);   // every member can view the catalog
        return await db.ListRecipesAsync(q.WorkspaceId, q.Task, ct);
    }
}

public sealed record GetRecipeQuery(Guid WorkspaceId, string Name) : IQuery<RecipeRow>;

public sealed class GetRecipeQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetRecipeQuery, RecipeRow>
{
    public async ValueTask<RecipeRow> Handle(GetRecipeQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.GetRecipeAsync(q.WorkspaceId, q.Name, ct) ?? throw new NotFoundException("CATALOG_RECIPE_NOT_FOUND", "Recipe not found");
    }
}

/// <summary>Renders a trial command with sample values (S-17-2 Command tab, S-17-3) — same template resolver as selfcheck.</summary>
public sealed record RenderTemplatePreviewQuery(Guid WorkspaceId, string Recipe, string Entrypoint, string Task, bool WeightsEmpty)
    : IQuery<(string Command, string Error)>;

public sealed class RenderTemplatePreviewQueryHandler(IReadSession db, WorkspaceAuthorizer auth)
    : IQueryHandler<RenderTemplatePreviewQuery, (string Command, string Error)>
{
    public async ValueTask<(string Command, string Error)> Handle(RenderTemplatePreviewQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        var r = await db.GetRecipeAsync(q.WorkspaceId, q.Recipe, ct) ?? throw new NotFoundException("CATALOG_RECIPE_NOT_FOUND", "Recipe not found");
        var m = JsonNode.Parse(r.Manifest)!.AsObject();
        var cmd = m["entrypoints"]?[q.Entrypoint]?["command"]?.ToString()
                  ?? throw new NotFoundException("CATALOG_ENTRYPOINT_NOT_FOUND", $"Recipe has no entrypoint {q.Entrypoint}");
        var task = string.IsNullOrEmpty(q.Task) ? r.Tasks.FirstOrDefault() ?? "" : q.Task;
        try
        {
            var text = Template.Render(cmd.Replace('\n', ' ').Trim(), (string n, out JsonNode? v) =>
            {
                v = n switch
                {
                    "weights" or "weights.ref" => q.WeightsEmpty ? null : JsonValue.Create("/mnt/weights/pretrained"),
                    "task" => JsonValue.Create(task),
                    "nnodes" or "gpus_per_node" => JsonValue.Create(1),
                    "node_rank" => JsonValue.Create(0),
                    "gpu.supports_bf16" => JsonValue.Create(true),
                    _ when n.StartsWith("params.", StringComparison.Ordinal) =>
                        m["params"]?[n[7..]]?["default"]?.DeepClone() ?? JsonValue.Create($"<{n}>"),
                    _ => JsonValue.Create($"<{n}>"),
                };
                return true;
            });
            return (text, "");
        }
        catch (TemplateException e)
        {
            return ("", $"{e.Reason}: {e.Message}");
        }
    }
}
