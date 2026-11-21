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

public sealed record GetEvaluationQuery(Guid WorkspaceId, Guid EvaluationId) : IQuery<EvaluationRow>;

public sealed class GetEvaluationQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetEvaluationQuery, EvaluationRow>
{
    public async ValueTask<EvaluationRow> Handle(GetEvaluationQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var e = await db.GetEvaluationAsync(q.EvaluationId, ct);
        return e is not null && e.WorkspaceId == q.WorkspaceId ? e : throw new NotFoundException("EVAL_NOT_FOUND", "Evaluation not found");
    }
}
