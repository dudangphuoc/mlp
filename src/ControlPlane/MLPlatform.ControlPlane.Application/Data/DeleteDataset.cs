using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>Delete a dataset: it leaves every list and its labeling projects end.</summary>
[RequiresPermission("dataset.write")]
public sealed record DeleteDatasetCommand(Guid WorkspaceId, Guid DatasetId) : ICommand<bool>;

/// <summary>Marks the dataset and its labeling projects deleted.</summary>
public sealed class DeleteDatasetCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<DeleteDatasetCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteDatasetCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        _ = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        await db.SetDatasetStatusAsync(c.DatasetId, c.WorkspaceId, "deleted", ct);
        await db.SetProjectsStateOfDatasetAsync(c.DatasetId, c.WorkspaceId, "deleted", ct);
        return true;
    }
}
