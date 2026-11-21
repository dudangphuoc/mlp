using System.Globalization;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Registry;

[Access(AccessKind.Internal)]
public sealed record SetEvaluationStateCommand(Guid EvaluationId, string State, string? Reason, Guid? DeploymentId, int? Total = null) : ICommand<bool>, IInternalCommand;

public sealed class SetEvaluationStateCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<SetEvaluationStateCommand, bool>
{
    public async ValueTask<bool> Handle(SetEvaluationStateCommand c, CancellationToken ct)
    {
        await db.SetEvaluationStateAsync(c.EvaluationId, c.State, c.Reason, c.DeploymentId, clock.UtcNow, ct);
        if (c.Total is { } t) await db.SetEvaluationTotalAsync(c.EvaluationId, t, ct);
        if (c.State == "failed" && await db.GetEvaluationAsync(c.EvaluationId, ct) is { } e)
            await Notifier.SendAsync(db, clock, e.WorkspaceId, [e.CreatedBy], "model.evaluated", $"Model evaluation failed: {c.Reason}",
                $"#/models?eval={e.Id}", $"evaluation/{e.Id}", "error", ct);
        return true;
    }
}
