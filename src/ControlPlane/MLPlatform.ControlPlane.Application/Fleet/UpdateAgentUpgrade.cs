using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Orchestration loop updates job state (internal, individual steps are not audited).</summary>
public sealed record UpdateAgentUpgradeCommand(Guid Id, string State, string Phase, string Message) : ICommand<bool>, IInternalCommand;

/// <summary>Handles <see cref="UpdateAgentUpgradeCommand"/>.</summary>
public sealed class UpdateAgentUpgradeCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<UpdateAgentUpgradeCommand, bool>
{
    public async ValueTask<bool> Handle(UpdateAgentUpgradeCommand c, CancellationToken ct)
    {
        await db.UpdateAgentUpgradeAsync(c.Id, c.State, c.Phase, c.Message, clock.UtcNow, ct);
        return true;
    }
}
