using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>A one-hour download link of a finished conversion.</summary>
public sealed record GetExportDownloadQuery(Guid WorkspaceId, Guid ExportId) : IQuery<string>;

/// <summary>Signs the stored file for the browser.</summary>
public sealed class GetExportDownloadQueryHandler(IReadSession db, S3Client s3, WorkspaceAuthorizer auth) : IQueryHandler<GetExportDownloadQuery, string>
{
    public async ValueTask<string> Handle(GetExportDownloadQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var e = await db.GetExportAsync(q.WorkspaceId, q.ExportId, ct) ?? throw new NotFoundException("REGISTRY_EXPORT_NOT_FOUND", "Conversion not found");
        if (e.State != "ready") throw new PreconditionException("REGISTRY_EXPORT_NOT_READY", "The conversion has no file yet");
        return s3.Presign(HttpMethod.Get, Buckets.Artifacts, ExportRules.Prefix(q.WorkspaceId, e.Id) + e.FileName, TimeSpan.FromHours(1));
    }
}
