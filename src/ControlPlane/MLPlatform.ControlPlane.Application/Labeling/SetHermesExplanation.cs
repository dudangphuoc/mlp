using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Stores the AI explanation of a check (internal: the api authorized the caller through GetHermesCheckQuery
/// first).</summary>
[Access(AccessKind.Internal)]
public sealed record SetHermesExplanationCommand(Guid WorkspaceId, Guid CheckId, string Explanation, string Lang, Guid? RunId) : ICommand<bool>, IInternalCommand;

public sealed class SetHermesExplanationCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SetHermesExplanationCommand, bool>
{
    public async ValueTask<bool> Handle(SetHermesExplanationCommand c, CancellationToken ct)
    {
        await db.SetHermesExplanationAsync(c.CheckId, c.WorkspaceId, c.Explanation, c.Lang, c.RunId, clock.UtcNow, ct);
        return true;
    }
}
