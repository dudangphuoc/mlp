using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Scripts of git repositories startable from a project's page.</summary>
[Access(AccessKind.ScopedPermission, "run.read")]
public sealed record ListCodeEntriesQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<IReadOnlyList<CodeEntryRow>>;

public sealed class ListCodeEntriesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListCodeEntriesQuery, IReadOnlyList<CodeEntryRow>>
{
    public async ValueTask<IReadOnlyList<CodeEntryRow>> Handle(ListCodeEntriesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListCodeEntriesAsync(q.WorkspaceId, q.ProjectId, ct);
    }
}
