using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Serving;

/// <summary>Internal (scheduler, at replica launch): fresh model links for the version.</summary>
[Access(AccessKind.Internal)]
public sealed record ResolveModelFilesQuery(Guid WorkspaceId, Guid ModelVersionId) : IQuery<string>;

/// <summary>Builds the version's model links.</summary>
public sealed class ResolveModelFilesQueryHandler(IReadSession db, S3Client s3) : IQueryHandler<ResolveModelFilesQuery, string>
{
    public async ValueTask<string> Handle(ResolveModelFilesQuery q, CancellationToken ct) => await ModelFiles.BuildAsync(db, s3, q.ModelVersionId, q.WorkspaceId, ct);
}
