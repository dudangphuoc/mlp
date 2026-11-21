using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Builds the sample list (deterministic shuffle by evaluation id, then the first max_samples) with internal
/// download URLs.</summary>
public sealed record PlanEvaluationQuery(Guid EvaluationId) : IQuery<EvaluationPlan>;

public sealed class PlanEvaluationQueryHandler(IReadSession db, S3Client s3) : IQueryHandler<PlanEvaluationQuery, EvaluationPlan>
{
    public async ValueTask<EvaluationPlan> Handle(PlanEvaluationQuery q, CancellationToken ct)
    {
        var e = await db.GetEvaluationAsync(q.EvaluationId, ct) ?? throw new NotFoundException("EVAL_NOT_FOUND", "Evaluation not found");
        var mv = await db.GetModelVersionAsync(e.ModelVersionId, e.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        var v = await db.GetVersionAsync(e.DatasetVersionId, e.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        var files = await db.ListVersionFilesAsync(v.Id, ct);
        var urls = files.ToDictionary(f => f.Path, f => f.Url, StringComparer.Ordinal);
        string Url(string p) => DatasetFileUrls.For(s3, v, p, urls.GetValueOrDefault(p), Audience.Internal);
        var classes = Evaluations.Classes((await db.GetCurrentSchemaAsync(v.DatasetId, ct))?.Classes);
        var specs = EvalMath.FindSamples(mv.Task, urls.Keys, e.PathFilter);
        string? coco = null;
        if (EvalMath.DetectTasks.Contains(mv.Task) && specs.All(s => s.LabelPath is null))
            coco = urls.Keys.Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && (p.Contains("annotation", StringComparison.OrdinalIgnoreCase) || p.Contains("instances", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(p => !string.IsNullOrEmpty(e.PathFilter) && p.Contains(e.PathFilter, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (EvalMath.DetectTasks.Contains(mv.Task) && coco is null && specs.All(s => s.LabelPath is null))
            throw new PreconditionException("EVAL_NO_GROUND_TRUTH", "No labels found next to the images (YOLO txt or COCO json)");
        var seed = $"{e.DatasetVersionId}|{e.PathFilter}|{e.MaxSamples}";
        var chosen = specs.OrderBy(s => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed + ":" + s.ImagePath))), StringComparer.Ordinal)
            .Take(e.MaxSamples).ToList();
        var done = (await db.DoneSampleIndexesAsync(e.Id, ct)).Select(r => r.Idx).ToHashSet();
        return new EvaluationPlan(e, mv.Task, classes,
            chosen.Select((s, i) => (i, s.ImagePath, Url(s.ImagePath), s.LabelPath, s.LabelPath is null ? null : Url(s.LabelPath), s.ClassName)).ToList(),
            coco is null ? null : Url(coco), done);
    }
}
