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

public sealed record ListPromptsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<PromptRow>>;

public sealed class ListPromptsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListPromptsQuery, IReadOnlyList<PromptRow>>
{
    public async ValueTask<IReadOnlyList<PromptRow>> Handle(ListPromptsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListPromptsAsync(q.WorkspaceId, ct);
    }
}
