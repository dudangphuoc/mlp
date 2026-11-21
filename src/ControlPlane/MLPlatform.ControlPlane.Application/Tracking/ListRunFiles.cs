using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Files of the run's model directory.</summary>
public sealed record ListRunFilesQuery(Guid WorkspaceId, Guid RunId) : IQuery<IReadOnlyList<RunFile>>;

/// <summary>Lists the uploaded model files and signs a download link for each.</summary>
public sealed class ListRunFilesQueryHandler(IReadSession db, WorkspaceAuthorizer auth, MLPlatform.Storage.S3Client s3) : IQueryHandler<ListRunFilesQuery, IReadOnlyList<RunFile>>
{
    public async ValueTask<IReadOnlyList<RunFile>> Handle(ListRunFilesQuery q, CancellationToken ct)
    {
        var r = await RunRules.RequireAsync(db, auth, q.WorkspaceId, q.RunId, "run.read", ct);
        var prefix = RunArtifacts.Prefix(r.WorkspaceId, r.Id);
        return (await db.ListRunArtifactsAsync(r.Id, ct))
            .Select(f => new RunFile(f.Path, f.SizeBytes, s3.Presign(HttpMethod.Get, Buckets.Artifacts, prefix + f.Path, TimeSpan.FromHours(1))))
            .ToList();
    }
}
