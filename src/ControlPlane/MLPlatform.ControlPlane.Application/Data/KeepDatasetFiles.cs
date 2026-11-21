using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Undo "remove" for files of the latest version that are only marked.</summary>
public sealed record KeepDatasetFilesCommand(Guid WorkspaceId, Guid DatasetId, IReadOnlyList<string> Paths) : ICommand<bool>;

/// <summary>Drops the removal marks.</summary>
public sealed class KeepDatasetFilesCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<KeepDatasetFilesCommand, bool>
{
    public async ValueTask<bool> Handle(KeepDatasetFilesCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        foreach (var p in c.Paths) await db.DeletePendingRemovalAsync(c.DatasetId, Buckets.SafePath(p), ct);
        return true;
    }
}
