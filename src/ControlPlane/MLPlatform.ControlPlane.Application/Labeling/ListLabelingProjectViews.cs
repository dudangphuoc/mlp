using System.Globalization;
using System.Text;
using System.Text.Json;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Projects with progress and the caller's own open work (S-05-3) — a person sees what is waiting for them without opening each project.</summary>
public sealed record ListLabelingProjectViewsQuery(Guid WorkspaceId, Guid? DatasetId) : IQuery<IReadOnlyList<ProjectView>>;

/// <summary>Joins the project rows with one progress query and the dataset names.</summary>
public sealed class ListLabelingProjectViewsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListLabelingProjectViewsQuery, IReadOnlyList<ProjectView>>
{
    public async ValueTask<IReadOnlyList<ProjectView>> Handle(ListLabelingProjectViewsQuery q, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(q.WorkspaceId, "dataset.read", ct);
        var projects = await db.ListLabelProjectsAsync(q.WorkspaceId, q.DatasetId, ct);
        if (projects.Count == 0) return [];
        var sums = (await db.ProjectSummariesAsync(q.WorkspaceId, uid, ct)).ToDictionary(s => s.ProjectId);
        var names = (await db.ListDatasetsAsync(q.WorkspaceId, "", ct)).ToDictionary(d => d.Id, d => d.Name);
        var manage = await auth.HasAsync(q.WorkspaceId, "labeling.manage", ct);
        ProjectSummaryRow? Sum(Guid id) => sums.GetValueOrDefault(id) is { } s
            ? manage && !s.MyRoles.Split(',').Contains("lead") ? s with { MyRoles = s.MyRoles.Length > 0 ? s.MyRoles + ",lead" : "lead" } : s
            : null;
        return projects.Select(p => new ProjectView(p, Sum(p.Id), names.GetValueOrDefault(p.DatasetId, ""))).ToList();
    }
}
