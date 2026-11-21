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

public sealed record GetLabelSchemaQuery(Guid WorkspaceId, Guid DatasetId, int Version) : IQuery<SchemaRow>;

public sealed class GetLabelSchemaQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetLabelSchemaQuery, SchemaRow>
{
    public async ValueTask<SchemaRow> Handle(GetLabelSchemaQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        _ = await db.GetDatasetAsync(q.DatasetId, q.WorkspaceId, ct) ?? throw new NotFoundException("DATA_DATASET_NOT_FOUND", "Dataset not found");
        return (q.Version > 0 ? await db.GetSchemaVersionAsync(q.DatasetId, q.Version, ct) : await db.GetCurrentSchemaAsync(q.DatasetId, ct))
               ?? throw new NotFoundException("LABEL_SCHEMA_NOT_FOUND", "Dataset has no label schema yet");
    }
}
