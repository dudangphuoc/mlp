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

/// <summary>A one-hour link to a file of a run: a candidate model, a disagreement image, the report, the spec or the
/// script.</summary>
[RequiresPermission("run.read")]
public sealed record GetOptimizationFileQuery(Guid WorkspaceId, Guid Id, string Path) : IQuery<string>;

/// <summary>Only the files a run writes; no path escapes the run's folder.</summary>
public sealed class GetOptimizationFileQueryHandler(IReadSession db, S3Client s3, WorkspaceAuthorizer auth) : IQueryHandler<GetOptimizationFileQuery, string>
{
    private static readonly System.Text.RegularExpressions.Regex Allowed = new(@"^(?:(?:fp32|fp16|int8)/model\.onnx|worst/\d\.jpg|report\.json|spec\.json|pipeline\.py|config/onnx\.json|signature\.json)$");

    public async ValueTask<string> Handle(GetOptimizationFileQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        if (!Allowed.IsMatch(q.Path)) throw new ValidationException(new Dictionary<string, string> { ["path"] = "Not a file of an optimization" });
        _ = await db.GetOptimizationAsync(q.WorkspaceId, q.Id, ct) ?? throw new NotFoundException("OPT_NOT_FOUND", "Optimization not found");
        return s3.Presign(HttpMethod.Get, Buckets.Artifacts, OptimizationRules.Prefix(q.WorkspaceId, q.Id) + q.Path, TimeSpan.FromHours(1));
    }
}
