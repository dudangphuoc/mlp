using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Save a candidate as a new version of the source's model (recipe generic-onnx), with lineage, report and
/// checksums.</summary>
[RequiresPermission("model.write")]
public sealed record PromoteOptimizationCommand(Guid WorkspaceId, Guid Id, string Candidate) : ICommand<ModelVersionRow>;

/// <summary>Copies into models/{model}/v{n}/ : weights/model.onnx, config/onnx.json, classes.json (from the source),
/// signature.json, report.json, pipeline.py and SHA256SUMS.</summary>
public sealed class PromoteOptimizationCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<PromoteOptimizationCommand, ModelVersionRow>
{
    public async ValueTask<ModelVersionRow> Handle(PromoteOptimizationCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var o = await db.GetOptimizationAsync(c.WorkspaceId, c.Id, ct) ?? throw new NotFoundException("OPT_NOT_FOUND", "Optimization not found");
        if (o.State != "ready") throw new PreconditionException("OPT_NOT_READY", "The optimization has not finished");
        if ((JsonNode.Parse(o.Promoted) as JsonObject)?[c.Candidate]?.ToString() is { } existing && Guid.TryParse(existing, out var vid)
            && await db.GetModelVersionAsync(vid, c.WorkspaceId, ct) is { } already) return already;
        var report = JsonNode.Parse(o.Report) as JsonObject ?? [];
        if (report["candidates"]?[c.Candidate] is not JsonObject cand) throw new PreconditionException("OPT_NO_CANDIDATE", $"Candidate {c.Candidate} was not built");
        var src = await db.GetModelVersionAsync(o.SourceVersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var modelId = await db.GetModelVersionModelIdAsync(src.Id, c.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var srcPrefix = (await db.GetVersionPrefixAsync(src.Id, c.WorkspaceId, ct))!;
        var run = OptimizationRules.Prefix(c.WorkspaceId, o.Id);
        var n = await db.NextModelVersionAsync(modelId, ct);
        var prefix = $"{c.WorkspaceId}/models/{modelId}/v{n}/";
        var copies = new List<(string Bucket, string From, string To)>
        {
            (Buckets.Artifacts, run + $"{c.Candidate}/model.onnx", "weights/model.onnx"), (Buckets.Artifacts, run + "config/onnx.json", "config/onnx.json"),
            (Buckets.Artifacts, run + "signature.json", "signature.json"), (Buckets.Artifacts, run + "report.json", "report.json"),
            (Buckets.Artifacts, run + "pipeline.py", "pipeline.py"),
        };
        if (await s3.HeadAsync(Buckets.Artifacts, srcPrefix + "classes.json", ct) is not null) copies.Add((Buckets.Artifacts, srcPrefix + "classes.json", "classes.json"));
        var sums = new StringBuilder();
        long size = 0;
        foreach (var (bucket, from, to) in copies)
        {
            await s3.CopyAsync(bucket, from, Buckets.Artifacts, prefix + to, ct);
            var head = await s3.HeadAsync(Buckets.Artifacts, prefix + to, ct);
            size += head?.Size ?? 0;
            var sha = to == "weights/model.onnx" ? cand["sha256"]?.ToString()
                : await s3.GetAsync(Buckets.Artifacts, prefix + to, ct) is { } bytes ? Convert.ToHexStringLower(SHA256.HashData(bytes)) : null;
            if (sha is not null) sums.Append(sha).Append("  ").Append(to).Append('\n');
        }
        var sumsBytes = Encoding.UTF8.GetBytes(sums.ToString());
        await s3.PutAsync(Buckets.Artifacts, prefix + "SHA256SUMS", sumsBytes, "text/plain", ct);
        var lineage = new JsonObject
        {
            ["optimization_id"] = o.Id.ToString(), ["device"] = o.Device, ["precision"] = c.Candidate, ["host_id"] = o.HostId?.ToString(),
            ["source_version"] = src.Version, ["latency"] = cand["latency"]?.DeepClone(), ["fidelity"] = cand["fidelity"]?.DeepClone(),
            ["size_bytes"] = cand["size_bytes"]?.DeepClone(), ["sha256"] = cand["sha256"]?.DeepClone(),
        };
        var id = Ids.New();
        await db.InsertOptimizedVersionAsync(id, modelId, n, src.RunId, src.Task, prefix, copies.Count + 1, size + sumsBytes.Length, uid, clock.UtcNow,
            $"optimization:{o.Id}#{c.Candidate}", src.Id, lineage.ToJsonString(), ct);
        await db.SetOptimizationPromotedAsync(c.WorkspaceId, o.Id, c.Candidate, id, ct);
        return (await db.GetModelVersionAsync(id, c.WorkspaceId, ct))!;
    }
}
