using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Application.Common;

/// <summary>Enforces [RequiresPermission] on the request's workspace before the handler runs; denials still reach the
/// audit.</summary>
public sealed class AuthorizationBehavior(WorkspaceAuthorizer authorizer) : IRequestBehavior
{
    public async ValueTask<object?> Handle(RequestContext context, Func<ValueTask<object?>> next, CancellationToken ct)
    {
        if (context.Permission is { } permission && context.WorkspaceId is { } workspaceId && workspaceId != Guid.Empty)
            await authorizer.RequireAsync(workspaceId, permission, ct);
        return await next();
    }
}
