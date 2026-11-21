using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>
/// Reference version: records the files' source addresses without copying anything; the machine downloads them from the source
/// when a job uses the version. Immutability comes from the source being pinned (HF commit sha, fixed URLs/keys).
/// </summary>
public sealed record CreateReferenceVersionCommand(Guid WorkspaceId, Guid DatasetId, string Note, string SourceRef,
    IReadOnlyList<(string Path, long Size, string Url)> Files) : ICommand<VersionRow>;

public sealed class CreateReferenceVersionCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth)
    : ICommandHandler<CreateReferenceVersionCommand, VersionRow>
{
    public async ValueTask<VersionRow> Handle(CreateReferenceVersionCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        if (c.Files.Count == 0) throw new PreconditionException("DATA_NOT_READY", "No files to reference");
        var n = await db.NextVersionNumberAsync(c.DatasetId, c.WorkspaceId, ct);
        var id = Ids.New();
        var now = clock.UtcNow;
        var size = c.Files.Sum(f => f.Size);
        await db.InsertReferenceVersionAsync(id, c.DatasetId, n, c.Files.Count, size, c.Note, uid, now, c.SourceRef, ct);
        foreach (var f in c.Files) await db.InsertVersionFileUrlAsync(id, Buckets.SafePath(f.Path), f.Size, f.Url, ct);
        return new VersionRow(id, c.DatasetId, n, "", c.Files.Count, size, c.Note, now, "reference", c.SourceRef);
    }
}
