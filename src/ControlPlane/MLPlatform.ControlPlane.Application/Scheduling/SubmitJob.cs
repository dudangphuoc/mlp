using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Scheduling;

// ---- Submit job (M03 §5.1) ----
public sealed record SubmitJobCommand(Guid WorkspaceId, string Kind, string Name, JobSpecDto Spec, int Priority = 50) : ICommand<Guid>;

public sealed class SubmitJobCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<SubmitJobCommand, Guid>
{
    public async ValueTask<Guid> Handle(SubmitJobCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "job.submit", ct);
        var errors = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(c.Spec.Image)) errors["image"] = "Image is required";
        if (c.Spec.Cpus <= 0) errors["cpus"] = "cpus must be > 0 (R1: a bundle always requests ≥ 1 CPU so it is not placed on the head)";
        if (c.Spec.Hosts.Count > 0 && c.Spec.Hosts.Count != c.Spec.Nnodes) errors["hosts"] = "Number of pinned machines must equal nnodes";
        if (errors.Count > 0) throw new ValidationException(errors);
        var id = Ids.New();
        await db.InsertJobAsync(id, c.WorkspaceId, c.Kind, c.Name, uid, c.Spec.ToJson(), c.Priority, clock.UtcNow, ct);
        await MLPlatform.ControlPlane.Application.Timeline.TimelineEvents.EmitAsync(db, clock, c.WorkspaceId, "api", "job", id, "job.queued", "info",
            $"Queued: needs {c.Spec.Nnodes} machines × {c.Spec.Cpus} CPU{(c.Spec.Gpus > 0 ? $", {c.Spec.Gpus} GPU" : "")}", ct: ct);
        return id;
    }
}
