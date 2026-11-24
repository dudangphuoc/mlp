using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Make a repository script startable from the web; the repository must be under a code source.</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record CreateCodeEntryCommand(Guid WorkspaceId, Guid ProjectId, string Name, string GitRemote, string Ref, string Script, string Machine,
    int Gpus, string Image) : ICommand<Guid>;

public sealed class CreateCodeEntryCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreateCodeEntryCommand, Guid>
{
    public async ValueTask<Guid> Handle(CreateCodeEntryCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        _ = await db.GetProjectAsync(c.ProjectId, c.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_PROJECT_NOT_FOUND", "Project not found");
        var remote = CodeSourceUrls.Normalize(c.GitRemote);
        if (CodeSourceUrls.Match(await db.ListCodeSourcesAsync(c.WorkspaceId, ct), remote) is null)
            throw new PreconditionException("TRACK_SOURCE_NOT_FOUND", $"No code source covers {remote}: add one in Settings › Integrations");
        var script = c.Script.Replace('\\', '/').Trim('/');
        if (c.Name.Trim().Length is 0 or > 60 || script.Length == 0 || script.Split('/').Contains(".."))
            throw new ValidationException(new Dictionary<string, string> { ["name"] = "Name 1–60 characters; script is a path inside the repository" });
        var id = Ids.New();
        try
        {
            await db.InsertCodeEntryAsync(id, c.WorkspaceId, c.ProjectId, c.Name.Trim(), remote, string.IsNullOrWhiteSpace(c.Ref) ? "main" : c.Ref.Trim(), script,
                string.IsNullOrWhiteSpace(c.Machine) ? "auto" : c.Machine.Trim(), Math.Max(0, c.Gpus), c.Image.Trim(), uid, clock.UtcNow, ct);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505")
        {
            throw new ConflictException("TRACK_ENTRY_EXISTS", $"'{c.Name.Trim()}' already exists in this project");
        }
        return id;
    }
}
