using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

[RequiresPermission("dataset.write")]
public sealed record CompleteUploadCommand(Guid WorkspaceId, Guid DatasetId, string Path) : ICommand<FileRow>;

public sealed class CompleteUploadCommandHandler(IWriteSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<CompleteUploadCommand, FileRow>
{
    public async ValueTask<FileRow> Handle(CompleteUploadCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        var path = Buckets.SafePath(c.Path);
        var head = await s3.HeadAsync(Buckets.Datasets, Buckets.Raw(c.WorkspaceId, c.DatasetId) + path, ct)
                   ?? throw new PreconditionException("DATA_UPLOAD_MISSING", "File not found in storage — upload has not finished");
        var now = clock.UtcNow;
        await db.UpsertFileAsync(c.DatasetId, path, head.Size, now, ct);
        return new FileRow(path, head.Size, now);
    }
}
