using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>
/// Existing labels of an item (the YOLO txt next to it in the version) become editable platform labels, once — the first time the item is
/// opened. Source "import". An item that already has platform labels is left alone.
/// </summary>
public sealed record ImportItemLabelsCommand(Guid WorkspaceId, Guid ProjectId, string Path, string YoloText) : ICommand<int>, IInternalCommand;

/// <summary>Parses the text, stores the boxes, marks the item imported (also when the file held no valid box).</summary>
public sealed class ImportItemLabelsCommandHandler(IWriteSession db, LabelAccess access, IClock clock) : ICommandHandler<ImportItemLabelsCommand, int>
{
    public async ValueTask<int> Handle(ImportItemLabelsCommand c, CancellationToken ct)
    {
        var (uid, p, _) = await access.LoadAsync(c.WorkspaceId, c.ProjectId, "dataset.read", ct);
        if (await db.LabelsImportedAsync(p.Id, c.Path, ct) == true || (await db.ListAnnotationsAsync(p.DatasetId, c.Path, ct)).Count > 0) return 0;
        var schema = await db.GetSchemaVersionAsync(p.DatasetId, p.SchemaVersion, ct);
        var boxes = LabelSeeding.ParseYolo(c.YoloText, schema is null ? null : LabelRules.ParseClasses(schema.Classes));
        if (boxes.Count > 0) await LabelStore.ReplaceAsync(db, clock, p, c.Path, uid, boxes, "import", ct);
        await db.MarkLabelsImportedAsync(p.Id, c.Path, ct);
        return boxes.Count;
    }
}
