using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

/// <summary>Renders a trial command with sample values (S-17-2 Command tab, S-17-3) — same template resolver as
/// selfcheck.</summary>
[RequiresPermission("dataset.read")]
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
