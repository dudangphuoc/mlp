using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

public sealed record CreateProjectCommand(Guid WorkspaceId, string Slug, string Name, string Task) : ICommand<ProjectRow>;

public sealed class CreateProjectCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreateProjectCommand, ProjectRow>
{
    public async ValueTask<ProjectRow> Handle(CreateProjectCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        if (!IdentityRules.Slug().IsMatch(c.Slug))
            throw new ValidationException(new Dictionary<string, string> { ["slug"] = "Invalid slug" });
        var id = Ids.New();
        try
        {
            await db.InsertProjectAsync(id, c.WorkspaceId, c.Slug, c.Name, c.Task, uid, clock.UtcNow, ct);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505")
        {
            throw new ConflictException("TRACK_PROJECT_EXISTS", "Project URL is already in use");
        }
        return new ProjectRow(id, c.WorkspaceId, c.Slug, c.Name, c.Task, clock.UtcNow);
    }
}
