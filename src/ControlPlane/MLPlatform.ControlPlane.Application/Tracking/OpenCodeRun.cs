using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>SDK: open a run for the developer's own code, here or (with <see cref="Remote"/>) on a cluster machine.
/// The project is created on first use.</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record OpenCodeRunCommand(string? Workspace, string Project, string? Name, string ParamsJson, string HostName, string CodeJson,
    RemoteCodeRun? Remote = null) : ICommand<CodeRunOpened>;

public sealed class OpenCodeRunCommandHandler(IWriteSession db, IClock clock, ICurrentUser user, WorkspaceAuthorizer auth, RemoteCodeRunner remote)
    : ICommandHandler<OpenCodeRunCommand, CodeRunOpened>
{
    public async ValueTask<CodeRunOpened> Handle(OpenCodeRunCommand c, CancellationToken ct)
    {
        var workspace = await CodeRunRules.PickWorkspaceAsync(db, user, c.Workspace, ct);
        var uid = await auth.RequireAsync(workspace.Id, "job.submit", ct);
        var project = await ProjectAsync(workspace.Id, c.Project.Trim().ToLowerInvariant(), uid, ct);
        var id = Ids.New();
        var number = (int)await db.NextRunNumberAsync(project.Id, workspace.Id, ct);
        var name = string.IsNullOrWhiteSpace(c.Name) ? "run" : c.Name.Trim()[..Math.Min(80, c.Name.Trim().Length)];
        var run = new NewCodeRun(id, workspace.Id, project, name, uid, JsonOrEmpty(c.ParamsJson), number, JsonOrEmpty(c.CodeJson));
        if (c.Remote is { } r) await remote.StartAsync(run, r, ct);
        else
            await db.InsertCodeRunAsync(id, project.Id, workspace.Id, name, uid, project.Task, run.ParamsJson, clock.UtcNow, number,
                c.HostName.Trim(), run.CodeJson, ct);
        return new CodeRunOpened(id, workspace.Id, workspace.Slug, project.Id, project.Slug, number);
    }

    private async Task<ProjectRow> ProjectAsync(Guid workspaceId, string slug, Guid uid, CancellationToken ct)
    {
        if (!IdentityRules.Slug().IsMatch(slug))
            throw new PreconditionException("TRACK_PROJECT_SLUG", $"Project '{slug}': use lowercase letters, digits and dashes");
        if (await db.GetProjectBySlugAsync(workspaceId, slug, ct) is { } existing) return existing;
        await db.InsertProjectAsync(Ids.New(), workspaceId, slug, slug, CodeRunRules.DefaultTask, uid, clock.UtcNow, ct);
        return await db.GetProjectBySlugAsync(workspaceId, slug, ct) ?? throw new NotFoundException("TRACK_PROJECT_NOT_FOUND", "Project not found");
    }

    private static string JsonOrEmpty(string json) => string.IsNullOrWhiteSpace(json) ? "{}" : json;
}
