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

/// <summary>Per-sample results for the gallery, worst first, with a browser-loadable image URL.</summary>
public sealed record ListEvalSamplesQuery(Guid WorkspaceId, Guid EvaluationId, bool ErrorsOnly, int Offset, int Limit)
    : IQuery<IReadOnlyList<(EvalSampleRow Sample, string ImageUrl)>>;

public sealed class ListEvalSamplesQueryHandler(IReadSession db, WorkspaceAuthorizer auth, S3Client s3)
    : IQueryHandler<ListEvalSamplesQuery, IReadOnlyList<(EvalSampleRow Sample, string ImageUrl)>>
{
    public async ValueTask<IReadOnlyList<(EvalSampleRow Sample, string ImageUrl)>> Handle(ListEvalSamplesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var e = await db.GetEvaluationAsync(q.EvaluationId, ct);
        if (e is null || e.WorkspaceId != q.WorkspaceId) throw new NotFoundException("EVAL_NOT_FOUND", "Evaluation not found");
        var v = (await db.GetVersionAsync(e.DatasetVersionId, q.WorkspaceId, ct))!;
        var urls = (await db.ListVersionFilesAsync(v.Id, ct)).ToDictionary(f => f.Path, f => f.Url, StringComparer.Ordinal);
        var rows = await db.ListEvalSamplesAsync(e.Id, q.ErrorsOnly, Math.Max(0, q.Offset), Math.Clamp(q.Limit, 1, 200), ct);
        return rows.Select(r => (r, DatasetFileUrls.For(s3, v, r.Path, urls.GetValueOrDefault(r.Path), Audience.Public))).ToList();
    }
}
