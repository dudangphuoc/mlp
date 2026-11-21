using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Delete a labeling project. The labels stay with the dataset (they belong to its files, not to the project).</summary>
public sealed record DeleteLabelingProjectCommand(Guid WorkspaceId, Guid ProjectId) : ICommand<bool>;

/// <summary>Marks the project deleted; it leaves every list.</summary>
public sealed class DeleteLabelingProjectCommandHandler(IWriteSession db, LabelAccess access) : ICommandHandler<DeleteLabelingProjectCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteLabelingProjectCommand c, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "labeling.manage", ct);
        await db.SetProjectStateAsync(p.Id, "deleted", ct);
        return true;
    }
}
