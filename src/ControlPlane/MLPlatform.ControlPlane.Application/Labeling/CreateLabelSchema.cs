using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

// ───────────── Schema ─────────────

[RequiresPermission("dataset.write")]
public sealed record CreateLabelSchemaCommand(Guid WorkspaceId, Guid DatasetId, string Task, string Classes) : ICommand<SchemaRow>;

public sealed class CreateLabelSchemaCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreateLabelSchemaCommand, SchemaRow>
{
    public async ValueTask<SchemaRow> Handle(CreateLabelSchemaCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "dataset.write", ct);
        var ds = await db.GetDatasetAsync(c.DatasetId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        LabelRules.ParseClasses(c.Classes);
        await db.InsertSchemaAsync(Ids.New(), c.DatasetId, string.IsNullOrEmpty(c.Task) ? ds.Task : c.Task, c.Classes, uid, clock.UtcNow, ct);
        return (await db.GetCurrentSchemaAsync(c.DatasetId, ct))!;
    }
}
