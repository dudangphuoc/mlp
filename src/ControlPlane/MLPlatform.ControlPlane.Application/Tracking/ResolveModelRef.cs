using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>SDK: where to download a model's files; from the developer's machine (member) or a run's container
/// (workload token).</summary>
[Access(AccessKind.ScopedPermission, "job.submit")]
public sealed record ResolveModelRefCommand(Guid WorkspaceId, Guid? RunId, string? WorkloadToken, string Reference) : ICommand<ModelFiles>, IInternalCommand;

public sealed class ResolveModelRefCommandHandler(IReadSession db, WorkspaceAuthorizer auth, TokenService tokens, IModelRefResolver resolver)
    : ICommandHandler<ResolveModelRefCommand, ModelFiles>
{
    public async ValueTask<ModelFiles> Handle(ResolveModelRefCommand c, CancellationToken ct)
    {
        var workspaceId = c is { WorkloadToken: { Length: > 0 } t, RunId: { } run }
            ? (await WorkloadAuth.RequireRunAsync(db, tokens, t, run, ct)).WorkspaceId
            : c.WorkspaceId;
        if (c.WorkloadToken is not { Length: > 0 }) await auth.RequireAsync(workspaceId, "job.submit", ct);
        return await resolver.ResolveAsync(workspaceId, c.Reference.Trim(), ct);
    }
}
