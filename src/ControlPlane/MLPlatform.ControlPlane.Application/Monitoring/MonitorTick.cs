using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Monitoring;

/// <summary>Called by the monitoring loop: runs only when there are enough new predictions since the previous snapshot.</summary>
public sealed record MonitorTickCommand : ICommand<int>, IInternalCommand;

public sealed class MonitorTickCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<MonitorTickCommand, int>
{
    public async ValueTask<int> Handle(MonitorTickCommand c, CancellationToken ct)
    {
        var n = 0;
        foreach (var cfg in await db.ListEnabledConfigsAsync(ct))
        {
            var last = (await db.ListSnapshotsAsync(cfg.DeploymentId, 1, ct)).FirstOrDefault();
            if (await db.CountSinceAsync(cfg.DeploymentId, last?.At ?? DateTimeOffset.MinValue, ct) < cfg.MinSamples) continue;
            await DriftJob.RunAsync(db, clock, cfg.DeploymentId, 0, ct);
            n++;
        }
        return n;
    }
}
