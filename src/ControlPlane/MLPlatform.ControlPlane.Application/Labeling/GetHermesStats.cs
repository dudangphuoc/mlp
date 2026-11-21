using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

[Access(AccessKind.ScopedPermission, "labeling.annotate")]
public sealed record GetHermesStatsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<(ProjectRow Project, List<HermesStatRow> Stats, HashSet<string> Roles)>;

/// <remarks>Roles = the caller's project roles, plus "manage" when the workspace role allows labeling.manage.</remarks>
public sealed class GetHermesStatsQueryHandler(IReadSession db, LabelAccess access, WorkspaceAuthorizer auth)
    : IQueryHandler<GetHermesStatsQuery, (ProjectRow, List<HermesStatRow>, HashSet<string>)>
{
    public async ValueTask<(ProjectRow, List<HermesStatRow>, HashSet<string>)> Handle(GetHermesStatsQuery q, CancellationToken ct)
    {
        var (_, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        if (await auth.HasAsync(q.WorkspaceId, "labeling.manage", ct)) roles.Add("manage");
        return (p, await db.HermesStatsAsync(p.WorkspaceId, p.Id, ct), roles);
    }
}
