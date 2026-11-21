using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

public sealed record MarkNotebookStoppingCommand(Guid ServerId, string Reason) : ICommand<bool>;

public sealed class MarkNotebookStoppingCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<MarkNotebookStoppingCommand, bool>
{
    public async ValueTask<bool> Handle(MarkNotebookStoppingCommand c, CancellationToken ct)
    {
        await db.SetServerStateAsync(c.ServerId, "stopping", c.Reason, null, null, clock.UtcNow, ct);
        return true;
    }
}
