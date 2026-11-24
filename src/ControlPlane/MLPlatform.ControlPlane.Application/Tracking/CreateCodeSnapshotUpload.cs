using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>SDK: where to upload a code snapshot (tar.gz named by its SHA-256); nothing to upload when the workspace
/// already has it.</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record CreateCodeSnapshotUploadCommand(string? Workspace, string Sha256, long SizeBytes, bool Data = false) : ICommand<SnapshotUploadTarget>;

public sealed class CreateCodeSnapshotUploadCommandHandler(IReadSession db, ICurrentUser user, WorkspaceAuthorizer auth, S3Client s3)
    : ICommandHandler<CreateCodeSnapshotUploadCommand, SnapshotUploadTarget>
{
    public async ValueTask<SnapshotUploadTarget> Handle(CreateCodeSnapshotUploadCommand c, CancellationToken ct)
    {
        var workspace = await CodeRunRules.PickWorkspaceAsync(db, user, c.Workspace, ct);
        await auth.RequireAsync(workspace.Id, "job.submit", ct);
        var sha = c.Sha256.Trim().ToLowerInvariant();
        if (!CodeRunRules.IsSha256(sha)) throw new PreconditionException("TRACK_SNAPSHOT_SHA", "sha256 must be 64 hex characters");
        var limit = c.Data ? CodeRunRules.MaxDataBytes : CodeRunRules.MaxSnapshotBytes;
        if (c.SizeBytes <= 0 || c.SizeBytes > limit)
            throw new PreconditionException("TRACK_SNAPSHOT_TOO_LARGE", $"{(c.Data ? "Local data" : "Code snapshot")} must be 1 byte to {limit >> 20} MB");
        var key = c.Data ? CodeRunRules.DataKey(workspace.Id, sha) : CodeRunRules.SnapshotKey(workspace.Id, sha);
        if (await s3.HeadAsync(Buckets.Artifacts, key, ct) is not null) return new SnapshotUploadTarget(workspace.Id, workspace.Slug, true, "");
        return new SnapshotUploadTarget(workspace.Id, workspace.Slug, false, s3.Presign(HttpMethod.Put, Buckets.Artifacts, key, TimeSpan.FromHours(1)));
    }
}
