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

public sealed record ListLabelingProjectsQuery(Guid WorkspaceId, Guid? DatasetId) : IQuery<IReadOnlyList<ProjectRow>>;

public sealed class ListLabelingProjectsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListLabelingProjectsQuery, IReadOnlyList<ProjectRow>>
{
    public async ValueTask<IReadOnlyList<ProjectRow>> Handle(ListLabelingProjectsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        return await db.ListLabelProjectsAsync(q.WorkspaceId, q.DatasetId, ct);
    }
}
