using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

[Access(AccessKind.ScopedPermission, "labeling.annotate")]
public sealed record GetLabelingStatsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<(ProjectRow Project, IReadOnlyList<StatRow> Stats)>;

public sealed class GetLabelingStatsQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<GetLabelingStatsQuery, (ProjectRow Project, IReadOnlyList<StatRow> Stats)>
{
    public async ValueTask<(ProjectRow Project, IReadOnlyList<StatRow> Stats)> Handle(GetLabelingStatsQuery q, CancellationToken ct)
    {
        var (_, p, _) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        return (p, await db.LabelProjectStatsAsync(p.Id, ct));
    }
}
