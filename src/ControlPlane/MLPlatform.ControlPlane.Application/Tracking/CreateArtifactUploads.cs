using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Presigned PUTs for files of a run's result: the container of its job (workload token), or the SDK of a code
/// run (user or API key, links reachable from the developer's machine).</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record CreateArtifactUploadsCommand(string? WorkloadToken, Guid RunId, IReadOnlyList<string> Paths) : ICommand<IReadOnlyList<string>>;

public sealed class CreateArtifactUploadsCommandHandler(IReadSession db, S3Client s3, TokenService tokens, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateArtifactUploadsCommand, IReadOnlyList<string>>
{
    public async ValueTask<IReadOnlyList<string>> Handle(CreateArtifactUploadsCommand c, CancellationToken ct)
    {
        var (run, audience) = await ArtifactWriters.RequireAsync(db, tokens, auth, c.WorkloadToken, c.RunId, ct);
        if (c.Paths.Count > 10_000) throw new ResourceExhaustedException("TRACK_TOO_MANY_FILES", "Too many artifact files");
        return c.Paths.Select(p => s3.Presign(HttpMethod.Put, Buckets.Artifacts, RunArtifacts.Prefix(run.WorkspaceId, run.Id) + Buckets.SafePath(p),
            TimeSpan.FromHours(6), audience)).ToList();
    }
}
