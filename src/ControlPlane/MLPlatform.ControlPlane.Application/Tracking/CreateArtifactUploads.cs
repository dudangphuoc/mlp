using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Container (workload token of the same job) requests presigned PUTs for files in the model directory (20/08
/// §5.1 step 7).</summary>
public sealed record CreateArtifactUploadsCommand(string WorkloadToken, Guid RunId, IReadOnlyList<string> Paths) : ICommand<IReadOnlyList<string>>;

public sealed class CreateArtifactUploadsCommandHandler(IReadSession db, S3Client s3, TokenService tokens)
    : ICommandHandler<CreateArtifactUploadsCommand, IReadOnlyList<string>>
{
    public async ValueTask<IReadOnlyList<string>> Handle(CreateArtifactUploadsCommand c, CancellationToken ct)
    {
        var run = await WorkloadAuth.RequireRunAsync(db, tokens, c.WorkloadToken, c.RunId, ct);
        if (c.Paths.Count > 10_000) throw new ResourceExhaustedException("TRACK_TOO_MANY_FILES", "Too many artifact files");
        return c.Paths.Select(p => s3.Presign(HttpMethod.Put, Buckets.Artifacts, RunArtifacts.Prefix(run.WorkspaceId, run.Id) + Buckets.SafePath(p),
            TimeSpan.FromHours(6), Audience.Workload)).ToList();
    }
}
