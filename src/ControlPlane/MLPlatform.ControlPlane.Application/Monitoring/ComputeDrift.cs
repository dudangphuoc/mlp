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

// ───────────── Drift + quality ─────────────

/// <summary>Computes a drift/quality snapshot over the `last_n` most recent predictions (0 = window_size); records the
/// snapshot; transition to alert → M16.</summary>
public sealed record ComputeDriftCommand(Guid WorkspaceId, Guid DeploymentId, int LastN) : ICommand<SnapshotRow>;

public sealed class ComputeDriftCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth, IEnumerable<IDriftListener> listeners) : ICommandHandler<ComputeDriftCommand, SnapshotRow>
{
    public async ValueTask<SnapshotRow> Handle(ComputeDriftCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "run.read", ct);
        return await DriftJob.RunAsync(db, clock, listeners, c.DeploymentId, c.LastN, ct);
    }
}
