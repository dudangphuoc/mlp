using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record GetAssistSettingsQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<(ProjectRow Project, ProjectAssistSettings Settings, bool CanManage)>;

public sealed class GetAssistSettingsQueryHandler(IReadSession db, LabelAccess access, WorkspaceAuthorizer auth)
    : IQueryHandler<GetAssistSettingsQuery, (ProjectRow, ProjectAssistSettings, bool)>
{
    public async ValueTask<(ProjectRow, ProjectAssistSettings, bool)> Handle(GetAssistSettingsQuery q, CancellationToken ct)
    {
        var (_, p, roles) = await access.LoadAsync(q.WorkspaceId, q.ProjectId, "dataset.read", ct);
        var manage = roles.Contains("lead") || await auth.HasAsync(q.WorkspaceId, "labeling.manage", ct);
        return (p, ProjectAssistSettings.From(await db.GetAssistSettingsAsync(p.Id, p.WorkspaceId, ct)), manage);
    }
}
