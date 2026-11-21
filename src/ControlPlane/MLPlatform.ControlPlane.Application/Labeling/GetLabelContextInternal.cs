using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Context for a background proposal (no caller: the job creator was authorized when the job
/// started).</summary>
[Access(AccessKind.Internal)]
public sealed record GetLabelContextInternalQuery(Guid ProjectId, string Path, Guid Actor) : IQuery<HermesContext>;

public sealed class GetLabelContextInternalQueryHandler(IReadSession db) : IQueryHandler<GetLabelContextInternalQuery, HermesContext>
{
    public async ValueTask<HermesContext> Handle(GetLabelContextInternalQuery q, CancellationToken ct)
    {
        var p = await db.GetProjectInternalAsync(q.ProjectId, ct) ?? throw new NotFoundException("LABEL_PROJECT_NOT_FOUND", "Labeling project not found");
        return await LabelContextLoader.LoadAsync(db, p, q.Path, q.Actor, ct);
    }
}
