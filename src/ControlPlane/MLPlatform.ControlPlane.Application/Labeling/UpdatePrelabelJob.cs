using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record UpdatePrelabelJobCommand(Guid Id, int Done, int Failed, string State, string? Error) : ICommand<bool>, IInternalCommand;

public sealed class UpdatePrelabelJobCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<UpdatePrelabelJobCommand, bool>
{
    public async ValueTask<bool> Handle(UpdatePrelabelJobCommand c, CancellationToken ct)
    {
        await db.UpdatePrelabelJobAsync(c.Id, c.Done, c.Failed, c.State, c.Error, clock.UtcNow, ct);
        return true;
    }
}
