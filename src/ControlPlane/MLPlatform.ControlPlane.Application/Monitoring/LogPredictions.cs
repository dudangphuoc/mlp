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

// ───────────── Gateway: prediction log + ground-truth labels ─────────────

/// <summary>Gateway logs each prediction (no user, no audit).</summary>
[Access(AccessKind.Internal)]
public sealed record LogPredictionsCommand(Guid DeploymentId, int ModelVersion, double LatencyMs, IReadOnlyList<(string Features, string Prediction)> Items)
    : ICommand<IReadOnlyList<Guid>>, IInternalCommand;

public sealed class LogPredictionsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<LogPredictionsCommand, IReadOnlyList<Guid>>
{
    public async ValueTask<IReadOnlyList<Guid>> Handle(LogPredictionsCommand c, CancellationToken ct)
    {
        List<Guid> ids = [with(capacity: c.Items.Count)];
        var now = clock.UtcNow;
        var each = c.LatencyMs / Math.Max(1, c.Items.Count);
        foreach (var (features, prediction) in c.Items)
        {
            var id = Ids.New();
            await db.InsertPredictionAsync(id, c.DeploymentId, c.ModelVersion, now, each, features, prediction, ct);
            ids.Add(id);
        }
        return ids;
    }
}
