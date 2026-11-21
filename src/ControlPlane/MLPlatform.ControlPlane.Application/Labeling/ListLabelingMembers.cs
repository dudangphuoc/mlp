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

public sealed record ListLabelingMembersQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<IReadOnlyList<ProjectMemberRow>>;

public sealed class ListLabelingMembersQueryHandler(IReadSession db, LabelAccess access) : IQueryHandler<ListLabelingMembersQuery, IReadOnlyList<ProjectMemberRow>>
{
    public async ValueTask<IReadOnlyList<ProjectMemberRow>> Handle(ListLabelingMembersQuery q, CancellationToken ct)
    {
        _ = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        return await db.ListProjectMembersAsync(q.ProjectId, ct);
    }
}
