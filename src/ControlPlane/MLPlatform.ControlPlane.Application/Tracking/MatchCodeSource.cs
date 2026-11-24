using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>SDK: whether the server can fetch this git remote itself (then only changed files are uploaded).</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record MatchCodeSourceQuery(string? Workspace, string GitRemote) : IQuery<string>;

public sealed class MatchCodeSourceQueryHandler(IReadSession db, ICurrentUser user, WorkspaceAuthorizer auth) : IQueryHandler<MatchCodeSourceQuery, string>
{
    public async ValueTask<string> Handle(MatchCodeSourceQuery q, CancellationToken ct)
    {
        var workspace = await CodeRunRules.PickWorkspaceAsync(db, user, q.Workspace, ct);
        await auth.RequireAsync(workspace.Id, "job.submit", ct);
        if (string.IsNullOrWhiteSpace(q.GitRemote)) return "";
        return CodeSourceUrls.Match(await db.ListCodeSourcesAsync(workspace.Id, ct), q.GitRemote)?.UrlPrefix ?? "";
    }
}
