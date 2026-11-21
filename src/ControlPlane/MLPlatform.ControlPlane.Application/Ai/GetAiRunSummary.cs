using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

public sealed record GetAiRunSummaryQuery(Guid? WorkspaceId) : IQuery<AiRunSummaryRow>;

public sealed class GetAiRunSummaryQueryHandler(IReadSession db, WorkspaceAuthorizer auth, ICurrentUser user, IClock clock) : IQueryHandler<GetAiRunSummaryQuery, AiRunSummaryRow>
{
    public async ValueTask<AiRunSummaryRow> Handle(GetAiRunSummaryQuery q, CancellationToken ct)
    {
        var (ws, instance) = await AiRunAccess.ScopeAsync(db, auth, user, q.WorkspaceId, ct);
        return await db.AiRunSummaryAsync(ws, instance, clock.UtcNow.AddHours(-1), ct) ?? new AiRunSummaryRow(0, 0, 0, null);
    }
}
