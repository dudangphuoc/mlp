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

/// <summary>POST /feedback: attaches a late-arriving ground-truth label to a prediction (only predictions of that same
/// deployment).</summary>
[Access(AccessKind.Internal)]
public sealed record RecordFeedbackCommand(Guid DeploymentId, IReadOnlyList<(Guid PredictionId, string GroundTruth)> Items) : ICommand<long>, IInternalCommand;

public sealed class RecordFeedbackCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordFeedbackCommand, long>
{
    public async ValueTask<long> Handle(RecordFeedbackCommand c, CancellationToken ct)
    {
        long n = 0;
        foreach (var (id, gt) in c.Items) n += await db.SetGroundTruthAsync(id, c.DeploymentId, gt, clock.UtcNow, ct);
        return n;
    }
}
