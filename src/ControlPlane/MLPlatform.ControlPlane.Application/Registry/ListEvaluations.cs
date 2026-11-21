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

public sealed record ListEvaluationsQuery(Guid WorkspaceId, Guid ModelVersionId) : IQuery<IReadOnlyList<EvaluationRow>>;

public sealed class ListEvaluationsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListEvaluationsQuery, IReadOnlyList<EvaluationRow>>
{
    public async ValueTask<IReadOnlyList<EvaluationRow>> Handle(ListEvaluationsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListEvaluationsAsync(q.WorkspaceId, q.ModelVersionId, ct);
    }
}
