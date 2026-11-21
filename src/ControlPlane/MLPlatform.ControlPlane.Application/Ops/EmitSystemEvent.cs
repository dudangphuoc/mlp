using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Instance-wide events (background loop stalled, component disconnected…) — 20/10 §2.6.</summary>
[Access(AccessKind.Internal)]
public sealed record EmitSystemEventCommand(string Source, string Kind, string Severity, string Message) : ICommand<bool>, IInternalCommand;

public sealed class EmitSystemEventCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<EmitSystemEventCommand, bool>
{
    public async ValueTask<bool> Handle(EmitSystemEventCommand c, CancellationToken ct)
    {
        await TimelineEvents.EmitAsync(db, clock, null, c.Source, "system", null, c.Kind, c.Severity, c.Message, ct: ct);
        return true;
    }
}
