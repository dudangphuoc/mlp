using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Hub;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Pipelines;

[Access(AccessKind.Internal)]
public sealed record FireIntervalTriggersCommand : ICommand<int>, IInternalCommand;

public sealed class FireIntervalTriggersCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<FireIntervalTriggersCommand, int>
{
    public async ValueTask<int> Handle(FireIntervalTriggersCommand c, CancellationToken ct) => await PipelineTriggers.FireAsync(db, clock, "", Guid.Empty, null, ct);
}
