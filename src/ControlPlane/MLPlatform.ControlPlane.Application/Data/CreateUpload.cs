using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Browser/SDK uploads directly to S3 storage (the api only issues URLs — 20/01 §3). P1: single PUT ≤ 5 GiB; multipart later.</summary>
public sealed record CreateUploadCommand(Guid WorkspaceId, Guid DatasetId, string Path, long SizeBytes) : ICommand<(string Url, DateTimeOffset ExpiresAt)>;

public sealed class CreateUploadCommandHandler(IReadSession db, S3Client s3, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateUploadCommand, (string Url, DateTimeOffset ExpiresAt)>
{
    public async ValueTask<(string Url, DateTimeOffset ExpiresAt)> Handle(CreateUploadCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        if (c.SizeBytes > 5L * 1024 * 1024 * 1024) throw new ResourceExhaustedException("DATA_TOO_LARGE", "Files > 5 GiB require multipart upload (not supported yet)");
        var key = Buckets.Raw(c.WorkspaceId, c.DatasetId) + Buckets.SafePath(c.Path);
        var ttl = TimeSpan.FromHours(1);
        return (s3.Presign(HttpMethod.Put, Buckets.Datasets, key, ttl), clock.UtcNow + ttl);
    }
}
