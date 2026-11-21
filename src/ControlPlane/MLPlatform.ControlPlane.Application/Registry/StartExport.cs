using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Convert a model version to <paramref name="Format"/>: submits a job that runs the recipe's export command.</summary>
public sealed record StartExportCommand(Guid WorkspaceId, Guid ModelVersionId, string Format, Guid? HostId = null) : ICommand<ExportRow>;

/// <summary>Builds the shim spec (entrypoint <c>export</c>), signs the upload links and submits the job.</summary>
public sealed class StartExportCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth, IMediator mediator) : ICommandHandler<StartExportCommand, ExportRow>
{
    public async ValueTask<ExportRow> Handle(StartExportCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var v = await db.GetModelVersionAsync(c.ModelVersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var recipe = await mediator.Query(new GetRecipeQuery(c.WorkspaceId, v.Recipe), ct);
        var manifest = JsonNode.Parse(recipe.Manifest)!.AsObject();
        if (ExportRules.Derived.Contains(c.Format) && !ExportRules.Formats(manifest).Contains(c.Format))
            return await StartDerivedAsync(c, v, uid, ct);
        if (!ExportRules.Formats(manifest).Contains(c.Format))
            throw new PreconditionException("REGISTRY_EXPORT_FORMAT", $"Recipe {recipe.Name} cannot convert to {c.Format}");
        if (recipe.State == "invalid") throw new PreconditionException("CATALOG_RECIPE_NOT_VERIFIED", $"Recipe {recipe.Name} is not valid ({recipe.State})");
        var variant = (manifest["variants"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(x => x["name"]?.ToString() == v.Variant);
        var id = Ids.New();
        var prefix = ExportRules.Prefix(c.WorkspaceId, id);
        var needsGpu = c.Format is "engine";   // TensorRT engines are built on a GPU; every other format converts on CPU
        var shimSpec = new JsonObject
        {
            ["manifest"] = manifest.DeepClone(), ["entrypoint"] = "export", ["task"] = v.Task, ["variant"] = variant?.DeepClone() ?? new JsonObject { ["name"] = v.Variant },
            ["params"] = new JsonObject(), ["output"] = "/mnt/output", ["model"] = "/mnt/model", ["format"] = c.Format, ["recipeSrc"] = "/opt/recipe/src",
            ["hardware"] = new JsonObject { ["device"] = needsGpu ? "cuda" : "cpu" },
        };
        var name = $"{v.ModelName}-v{v.Version}";
        var spec = new JobSpecDto
        {
            Image = $"recipes/{recipe.Name}:current", Command = ["python", "-c", "import os;exec(os.environ['MLP_EXPORT_SCRIPT'])"],
            Cpus = 2, Gpus = needsGpu ? 1 : 0, TimeoutSeconds = 3600,
            Env = new Dictionary<string, string>
            {
                ["MLP_EXPORT_SCRIPT"] = ExportRules.Script, ["MLP_JOB_SPEC"] = shimSpec.ToJsonString(),
                ["MLP_MODEL_FILES"] = await ModelFiles.BuildAsync(db, s3, v.Id, c.WorkspaceId, ct),
                ["MLP_EXPORT_NAME"] = name, ["MLP_EXPORT_FORMAT"] = c.Format,
                ["MLP_EXPORT_PUT"] = s3.Presign(HttpMethod.Put, Buckets.Artifacts, prefix + ExportRules.Staged, TimeSpan.FromHours(24), Audience.Workload),
                ["MLP_EXPORT_META_PUT"] = s3.Presign(HttpMethod.Put, Buckets.Artifacts, prefix + ExportRules.Meta, TimeSpan.FromHours(24), Audience.Workload),
            },
        };
        var jobId = await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "export", $"export-{name}-{c.Format}", spec), ct);
        await db.InsertExportAsync(id, c.WorkspaceId, v.Id, c.Format, jobId, uid, clock.UtcNow, ct);
        return (await db.GetExportAsync(c.WorkspaceId, id, ct))!;
    }

    /// <summary>Format built from the version's ready ONNX export in the generic-onnx image (TensorRT on a GPU of the chosen machine).</summary>
    private async Task<ExportRow> StartDerivedAsync(StartExportCommand c, ModelVersionRow v, Guid uid, CancellationToken ct)
    {
        var onnx = (await db.ListReadyOnnxExportsAsync(c.WorkspaceId, v.Id, ct)).FirstOrDefault()
                   ?? throw new PreconditionException("REGISTRY_EXPORT_NEEDS_ONNX", "Convert this version to ONNX first");
        var gpu = c.Format.StartsWith("tensorrt-", StringComparison.Ordinal);
        var id = Ids.New();
        var prefix = ExportRules.Prefix(c.WorkspaceId, id);
        var name = $"{v.ModelName}-v{v.Version}";
        var spec = new JobSpecDto
        {
            Image = "recipes/generic-onnx:current", Command = ["python3", "-c", "import os;exec(os.environ['MLP_EXPORT_SCRIPT'])"],
            Cpus = 2, Gpus = gpu ? 1 : 0, GpuMemoryMb = gpu ? 4096 : 0, TimeoutSeconds = 3600, Hosts = c.HostId is { } h ? [h] : [],
            Env = new Dictionary<string, string>
            {
                ["MLP_EXPORT_SCRIPT"] = ExportRules.DerivedScript, ["MLP_EXPORT_NAME"] = name, ["MLP_EXPORT_FORMAT"] = c.Format,
                ["MLP_ONNX_URL"] = s3.Presign(HttpMethod.Get, Buckets.Artifacts, ExportRules.Prefix(c.WorkspaceId, onnx.Id) + onnx.FileName, TimeSpan.FromHours(6), Audience.Workload),
                ["MLP_EXPORT_PUT"] = s3.Presign(HttpMethod.Put, Buckets.Artifacts, prefix + ExportRules.Staged, TimeSpan.FromHours(24), Audience.Workload),
                ["MLP_EXPORT_META_PUT"] = s3.Presign(HttpMethod.Put, Buckets.Artifacts, prefix + ExportRules.Meta, TimeSpan.FromHours(24), Audience.Workload),
            },
        };
        var jobId = await mediator.Send(new SubmitJobCommand(c.WorkspaceId, "export", $"export-{name}-{c.Format}", spec), ct);
        await db.InsertExportAsync(id, c.WorkspaceId, v.Id, c.Format, jobId, uid, clock.UtcNow, ct);
        return (await db.GetExportAsync(c.WorkspaceId, id, ct))!;
    }
}
