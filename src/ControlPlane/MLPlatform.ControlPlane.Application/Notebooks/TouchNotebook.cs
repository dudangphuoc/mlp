using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Notebooks;

public sealed record TouchNotebookCommand(Guid ServerId) : ICommand<bool>;

public sealed class TouchNotebookCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<TouchNotebookCommand, bool>
{
    public async ValueTask<bool> Handle(TouchNotebookCommand c, CancellationToken ct)
    {
        await db.TouchServerAsync(c.ServerId, clock.UtcNow, ct);
        return true;
    }
}
