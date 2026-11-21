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
public sealed record ListEvalRunsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<EvalRunRow>>;

public sealed class ListEvalRunsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListEvalRunsQuery, IReadOnlyList<EvalRunRow>>
{
    public async ValueTask<IReadOnlyList<EvalRunRow>> Handle(ListEvalRunsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListEvalRunsAsync(q.WorkspaceId, ct);
    }
}
