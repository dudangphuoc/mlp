using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Let the server fetch code from git repositories under this URL prefix (token sealed by the
/// caller).</summary>
[Access(AccessKind.ScopedPermission, "source.manage")]
public sealed record CreateCodeSourceCommand(Guid WorkspaceId, string UrlPrefix, string? SecretEnc) : ICommand<Guid>;

public sealed class CreateCodeSourceCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CreateCodeSourceCommand, Guid>
{
    public async ValueTask<Guid> Handle(CreateCodeSourceCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "source.manage", ct);
        var prefix = CodeSourceUrls.Normalize(c.UrlPrefix);
        if (!Uri.TryCreate(prefix, UriKind.Absolute, out var u) || u.Scheme != "https" || u.AbsolutePath.Trim('/').Length == 0)
            throw new ValidationException(new Dictionary<string, string> { ["url_prefix"] = "Use https://host/owner or https://host/owner/repo" });
        var id = Ids.New();
        try
        {
            await db.InsertCodeSourceAsync(id, c.WorkspaceId, prefix, c.SecretEnc, uid, clock.UtcNow, ct);
        }
        catch (Npgsql.PostgresException e) when (e.SqlState == "23505")
        {
            throw new ConflictException("TRACK_SOURCE_EXISTS", $"{prefix} is already a source");
        }
        return id;
    }
}
