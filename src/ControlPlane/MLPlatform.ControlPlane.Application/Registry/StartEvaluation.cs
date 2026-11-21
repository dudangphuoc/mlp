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

// ───────────── User commands / queries ─────────────

public sealed record StartEvaluationCommand(Guid WorkspaceId, Guid ModelVersionId, Guid DatasetVersionId, string PathFilter, int MaxSamples, double ConfThreshold)
    : ICommand<EvaluationRow>;

public sealed class StartEvaluationCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<StartEvaluationCommand, EvaluationRow>
{
    public async ValueTask<EvaluationRow> Handle(StartEvaluationCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "deployment.manage", ct);
        var v = await db.GetModelVersionAsync(c.ModelVersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("REGISTRY_VERSION_NOT_FOUND", "Model version not found");
        if (!EvalMath.Supports(v.Task))
            throw new PreconditionException("EVAL_TASK_UNSUPPORTED", $"Evaluation does not support task {v.Task} yet (supported: detect, classify, zero-shot image tasks)");
        _ = await db.GetVersionAsync(c.DatasetVersionId, c.WorkspaceId, ct) ?? throw new NotFoundException("DATA_VERSION_NOT_FOUND", "Dataset version not found");
        if (c.MaxSamples is < 1 or > Evaluations.MaxSamplesLimit)
            throw new ValidationException(new Dictionary<string, string> { ["max_samples"] = $"1–{Evaluations.MaxSamplesLimit}" });
        if (c.ConfThreshold is < 0.01 or > 0.99) throw new ValidationException(new Dictionary<string, string> { ["conf_threshold"] = "0.01–0.99" });
        var id = Ids.New();
        await db.InsertEvaluationAsync(id, c.WorkspaceId, v.Id, c.DatasetVersionId, c.PathFilter.Trim(), c.MaxSamples, c.ConfThreshold, uid, clock.UtcNow, ct);
        return (await db.GetEvaluationAsync(id, ct))!;
    }
}
