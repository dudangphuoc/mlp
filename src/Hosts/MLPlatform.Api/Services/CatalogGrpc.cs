using System.Text.Json.Nodes;
using Grpc.Core;
using MLPlatform.Api.Infrastructure;
using MLPlatform.ControlPlane.Infrastructure;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.Contracts.V1;
using MLPlatform.Cqrs;
using Empty = MLPlatform.Contracts.V1.Empty;


namespace MLPlatform.Api.Services;

public sealed class CatalogGrpc(IMediator mediator, RecipeEnvironments envs) : CatalogService.CatalogServiceBase
{
    private static Guid G(string s) =>
        Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { ["workspace_id"] = "Invalid UUID" });

    public override async Task<ListTasksResponse> ListTasks(Empty r, ServerCallContext ctx)
    {
        var res = new ListTasksResponse();
        foreach (var t in await mediator.Query(new ListTasksQuery(), ctx.CancellationToken))
        {
            var p = new TaskDef { Key = t.Key, ModalityIn = t.ModalityIn, ModalityOut = t.ModalityOut, PrimaryMetric = t.PrimaryMetric };
            p.Converters.AddRange(t.Converters);
            p.Metrics.AddRange(t.Metrics);
            res.Tasks.Add(p);
        }
        return res;
    }

    public override async Task<ListRecipesResponse> ListRecipes(ListRecipesRequest r, ServerCallContext ctx)
    {
        var res = new ListRecipesResponse();
        res.Recipes.AddRange((await mediator.Query(new ListRecipesQuery(G(r.WorkspaceId), r.Task), ctx.CancellationToken)).Select(x => ToProto(x, false)));
        return res;
    }

    public override async Task<Recipe> GetRecipe(GetRecipeRequest r, ServerCallContext ctx) =>
        ToProto(await mediator.Query(new GetRecipeQuery(G(r.WorkspaceId), r.Name), ctx.CancellationToken), true);

    public override async Task<RenderTemplateResponse> RenderTemplate(RenderTemplateRequest r, ServerCallContext ctx)
    {
        var (cmd, err) = await mediator.Query(new RenderTemplatePreviewQuery(G(r.WorkspaceId), r.Recipe, r.Entrypoint, r.Task, r.WeightsEmpty),
            ctx.CancellationToken);
        return new RenderTemplateResponse { Command = cmd, Error = err };
    }

    public override async Task<ListEnvironmentsResponse> ListEnvironments(RecipeEnvRequest r, ServerCallContext ctx)
    {
        var res = new ListEnvironmentsResponse();
        res.Environments.AddRange(await envs.ListAsync(mediator, G(r.WorkspaceId), r.Recipe, ctx.CancellationToken));
        return res;
    }

    public override Task<MLPlatform.Contracts.V1.Environment> BuildEnvironment(BuildEnvironmentRequest r, ServerCallContext ctx) =>
        envs.BuildAsync(mediator, G(r.WorkspaceId), r.Recipe, H(r.HostId) ?? throw new ValidationException(new Dictionary<string, string> { ["host_id"] = "Required" }),
            ctx.CancellationToken);

    public override Task<Inspection> GetInspection(GetInspectionRequest r, ServerCallContext ctx) =>
        envs.InspectAsync(mediator, G(r.WorkspaceId), r.Recipe, r.Variant, r.ParamsJson, r.NumClasses, H(r.HostId), r.Start, ctx.CancellationToken);

    public override async Task<ListRecipeFilesResponse> ListRecipeFiles(RecipeEnvRequest r, ServerCallContext ctx)
    {
        var res = new ListRecipeFilesResponse();
        res.Files.AddRange(await envs.ListFilesAsync(mediator, G(r.WorkspaceId), r.Recipe, ctx.CancellationToken));
        return res;
    }

    public override Task<RecipeFileContent> ReadRecipeFile(ReadRecipeFileRequest r, ServerCallContext ctx) =>
        envs.ReadFileAsync(mediator, G(r.WorkspaceId), r.Recipe, r.Path, ctx.CancellationToken);

    private static Guid? H(string s) =>
        s.Length == 0 ? null : Guid.TryParse(s, out var g) ? g : throw new ValidationException(new Dictionary<string, string> { ["host_id"] = "Invalid UUID" });

    private static Recipe ToProto(RecipeRow x, bool withManifest)
    {
        var m = JsonNode.Parse(x.Manifest)!.AsObject();
        var p = new Recipe
        {
            Id = x.Id.ToString(), Name = x.Name, DisplayName = x.DisplayName, Family = x.Family, Description = x.Description,
            Maturity = x.Maturity, LicenseCode = x.LicenseCode, LicenseWeights = x.LicenseWeights, LicenseNotes = x.LicenseNotes,
            RequiresAck = x.RequiresAck, Gpu = x.Gpu, MinVramGb = x.MinVramGb, Source = x.Source, State = x.State,
            Builtin = x.WorkspaceId is null, ManifestJson = withManifest ? x.Manifest : "",
        };
        p.Tasks.AddRange(x.Tasks);
        p.SelfcheckErrors.AddRange(x.Selfcheck);
        p.Entrypoints.AddRange((m["entrypoints"] as JsonObject)?.Select(kv => kv.Key) ?? []);
        foreach (var v in (m["variants"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var pv = new Variant
            {
                Name = v["name"]?.ToString() ?? "", DisplayName = v["displayName"]?.ToString() ?? v["name"]?.ToString() ?? "",
                License = v["license"]?.ToString() ?? x.LicenseWeights,
                ParamsM = v["params"] is JsonValue pm && pm.TryGetValue<double>(out var d) ? d : 0,
                MinVramGb = v["minVramGb"] is JsonValue vm && vm.TryGetValue<double>(out var vr) ? vr : x.MinVramGb,
                ReportedJson = v["reported"]?.ToJsonString() ?? "",
            };
            pv.Tasks.AddRange((v["tasks"] as JsonArray)?.Select(t => t!.ToString()) ?? x.Tasks);
            pv.RequiresZoo.AddRange((v["requiresFiles"] as JsonArray)?.Select(f => f?["zoo"]?.ToString() ?? "").Where(z => z.Length > 0) ?? []);
            p.Variants.Add(pv);
        }
        return p;
    }
}
