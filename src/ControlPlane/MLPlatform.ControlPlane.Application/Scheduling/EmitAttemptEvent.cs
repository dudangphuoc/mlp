using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

/// <summary>A visible step of an attempt on its machine (e.g.</summary>
[Access(AccessKind.Internal)]
public sealed record EmitAttemptEventCommand(Guid WorkspaceId, Guid AttemptId, string Code, string Level, string Message) : ICommand<bool>, IInternalCommand;

public sealed class EmitAttemptEventCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<EmitAttemptEventCommand, bool>
{
    public async ValueTask<bool> Handle(EmitAttemptEventCommand c, CancellationToken ct)
    {
        await MLPlatform.ControlPlane.Application.Timeline.TimelineEvents.EmitAsync(db, clock, c.WorkspaceId, "scheduler", "attempt", c.AttemptId, c.Code, c.Level, c.Message, ct: ct);
        return true;
    }
}
