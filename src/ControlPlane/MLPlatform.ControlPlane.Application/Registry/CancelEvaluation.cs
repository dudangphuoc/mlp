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

public sealed record CancelEvaluationCommand(Guid WorkspaceId, Guid EvaluationId) : ICommand<bool>;

public sealed class CancelEvaluationCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<CancelEvaluationCommand, bool>
{
    public async ValueTask<bool> Handle(CancelEvaluationCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        var e = await db.GetEvaluationAsync(c.EvaluationId, ct);
        if (e is null || e.WorkspaceId != c.WorkspaceId) throw new NotFoundException("EVAL_NOT_FOUND", "Evaluation not found");
        await db.SetEvaluationStateAsync(e.Id, "cancelled", "Cancelled by user", null, clock.UtcNow, ct);
        return true;
    }
}
