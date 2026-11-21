using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>Deletes the HF token.</summary>
[RequiresPermission("member.manage")]
public sealed record DeleteHubTokenCommand(Guid WorkspaceId) : ICommand<bool>;

/// <summary>Handles <see cref="DeleteHubTokenCommand"/>.</summary>
public sealed class DeleteHubTokenCommandHandler(IWriteSession db, WorkspaceAuthorizer auth) : ICommandHandler<DeleteHubTokenCommand, bool>
{
    public async ValueTask<bool> Handle(DeleteHubTokenCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "member.manage", ct);
        await db.DeleteIntegrationAsync(c.WorkspaceId, "huggingface", ct);
        return true;
    }
}
