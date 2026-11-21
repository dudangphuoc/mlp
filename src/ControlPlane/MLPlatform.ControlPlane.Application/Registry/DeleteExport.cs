using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Remove a conversion and its file (a running one: its job keeps running; cancel it on the Jobs page).</summary>
public sealed record DeleteExportCommand(Guid WorkspaceId, Guid ExportId) : ICommand<bool>;

/// <summary>Deletes the stored objects, then the row.</summary>
public sealed class DeleteExportCommandHandler(IWriteSession db, S3Client s3, WorkspaceAuthorizer auth) : ICommandHandler<DeleteExportCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteExportCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "model.write", ct);
        var e = await db.GetExportAsync(c.WorkspaceId, c.ExportId, ct) ?? throw new NotFoundException("REGISTRY_EXPORT_NOT_FOUND", "Conversion not found");
        foreach (var o in await s3.ListAsync(Buckets.Artifacts, ExportRules.Prefix(c.WorkspaceId, e.Id), ct)) await s3.DeleteAsync(Buckets.Artifacts, o.Key, ct);
        await db.DeleteExportAsync(c.WorkspaceId, e.Id, ct);
        return true;
    }
}
