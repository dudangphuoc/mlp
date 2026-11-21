using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

public sealed record GetAiRunQuery(Guid Id) : IQuery<AiRunRow>;

public sealed class GetAiRunQueryHandler(IReadSession db, WorkspaceAuthorizer auth, ICurrentUser user) : IQueryHandler<GetAiRunQuery, AiRunRow>
{
    public async ValueTask<AiRunRow> Handle(GetAiRunQuery q, CancellationToken ct)
    {
        var r = await db.GetAiRunAsync(q.Id, ct) ?? throw new NotFoundException("AI_RUN_NOT_FOUND", "AI run not found");
        var (_, instance) = await AiRunAccess.ScopeAsync(db, auth, user, r.WorkspaceId, ct);
        if (r.WorkspaceId is null && !instance) throw new NotFoundException("AI_RUN_NOT_FOUND", "AI run not found");
        return r;
    }
}
