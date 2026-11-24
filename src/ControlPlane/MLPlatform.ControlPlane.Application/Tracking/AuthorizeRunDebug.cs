using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Who may join a code run's debug relay: a member who may run jobs (developer side) or the run's own
/// container (workload token).</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record AuthorizeRunDebugQuery(Guid RunId, string? WorkloadToken) : IQuery<Guid>;

public sealed class AuthorizeRunDebugQueryHandler(IReadSession db, WorkspaceAuthorizer auth, TokenService tokens) : IQueryHandler<AuthorizeRunDebugQuery, Guid>
{
    public async ValueTask<Guid> Handle(AuthorizeRunDebugQuery q, CancellationToken ct)
    {
        if (q.WorkloadToken is { Length: > 0 } t) return (await WorkloadAuth.RequireRunAsync(db, tokens, t, q.RunId, ct)).WorkspaceId;
        var run = await db.GetRunAsync(q.RunId, ct) ?? throw new NotFoundException("TRACK_RUN_NOT_FOUND", "Run not found");
        await CodeRunRules.RequireAsync(db, auth, run.WorkspaceId, run.Id, ct);
        return run.WorkspaceId;
    }
}
