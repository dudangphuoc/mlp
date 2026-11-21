using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

/// <summary>Model versions registered from a run — the run page offers Test / Evaluate on them right after
/// training.</summary>
public sealed record ListVersionsByRunQuery(Guid WorkspaceId, Guid RunId) : IQuery<IReadOnlyList<ModelVersionRow>>;

public sealed class ListVersionsByRunQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListVersionsByRunQuery, IReadOnlyList<ModelVersionRow>>
{
    public async ValueTask<IReadOnlyList<ModelVersionRow>> Handle(ListVersionsByRunQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListVersionsByRunAsync(q.RunId, q.WorkspaceId, ct);
    }
}
