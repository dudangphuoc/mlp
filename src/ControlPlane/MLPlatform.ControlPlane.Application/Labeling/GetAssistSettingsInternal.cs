using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Internal (background jobs): settings by project, no caller.</summary>
public sealed record GetAssistSettingsInternalQuery(Guid WorkspaceId, Guid ProjectId) : IQuery<ProjectAssistSettings>;

public sealed class GetAssistSettingsInternalQueryHandler(IReadSession db) : IQueryHandler<GetAssistSettingsInternalQuery, ProjectAssistSettings>
{
    public async ValueTask<ProjectAssistSettings> Handle(GetAssistSettingsInternalQuery q, CancellationToken ct) =>
        ProjectAssistSettings.From(await db.GetAssistSettingsAsync(q.ProjectId, q.WorkspaceId, ct));
}
