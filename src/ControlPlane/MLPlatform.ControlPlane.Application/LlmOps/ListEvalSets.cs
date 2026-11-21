using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.LlmOps;

[RequiresPermission("run.read")]
public sealed record ListEvalSetsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<EvalSetRow>>;

public sealed class ListEvalSetsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListEvalSetsQuery, IReadOnlyList<EvalSetRow>>
{
    public async ValueTask<IReadOnlyList<EvalSetRow>> Handle(ListEvalSetsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListEvalSetsAsync(q.WorkspaceId, ct);
    }
}
