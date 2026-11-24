using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Record uploaded result files of a run (same callers as
/// <see cref="CreateArtifactUploadsCommand"/>).</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record CompleteArtifactsCommand(string? WorkloadToken, Guid RunId, IReadOnlyList<(string Path, long Size)> Files) : ICommand<int>;

public sealed class CompleteArtifactsCommandHandler(IWriteSession db, TokenService tokens, WorkspaceAuthorizer auth) : ICommandHandler<CompleteArtifactsCommand, int>
{
    public async ValueTask<int> Handle(CompleteArtifactsCommand c, CancellationToken ct)
    {
        var (run, _) = await ArtifactWriters.RequireAsync(db, tokens, auth, c.WorkloadToken, c.RunId, ct);
        foreach (var (path, size) in c.Files) await db.UpsertRunArtifactAsync(run.Id, Buckets.SafePath(path), size, ct);
        return c.Files.Count;
    }
}
