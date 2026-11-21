using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Fleet;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>
/// One trial. <paramref name="LiveLines"/> returns the <c>[mlp-trial]</c> lines the running attempt of a job has logged so far
/// (the host keeps the log; the application cannot read it itself).
/// </summary>
public sealed record GetTrialQuery(Guid WorkspaceId, Guid TrialId, Func<Guid, Task<IReadOnlyList<string>>>? LiveLines = null) : IQuery<TrialView>;

/// <summary>Reads the trial and, while it is active, its live stages.</summary>
public sealed class GetTrialQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetTrialQuery, TrialView>
{
    public async ValueTask<TrialView> Handle(GetTrialQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var t = await db.GetTrialAsync(q.TrialId, q.WorkspaceId, ct) ?? throw new NotFoundException("TRACK_TRIAL_NOT_FOUND", "Trial not found");
        IReadOnlyList<string>? lines = t.State is "queued" or "running" && t.JobId is { } j && q.LiveLines is { } live ? await live(j) : null;
        return await RecipeTrials.ViewAsync(db, t, lines, ct);
    }
}
