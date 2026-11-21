using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

/// <summary>Syncs builtin recipes + the task catalog from the release's `recipes/` directory (M17 §2, D-30).</summary>
[Access(AccessKind.Internal)]
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
