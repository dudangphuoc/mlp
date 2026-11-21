using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.ControlPlane.Application.Tracking;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tuning;

public sealed record GetSweepQuery(Guid WorkspaceId, Guid SweepId) : IQuery<(SweepRow Sweep, IReadOnlyList<TrialRow> Trials)>;

public sealed class GetSweepQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetSweepQuery, (SweepRow Sweep, IReadOnlyList<TrialRow> Trials)>
{
    public async ValueTask<(SweepRow Sweep, IReadOnlyList<TrialRow> Trials)> Handle(GetSweepQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var s = await db.GetSweepAsync(q.SweepId, q.WorkspaceId, ct) ?? throw new NotFoundException("TUNE_SWEEP_NOT_FOUND", "Sweep not found");
        return (s, await db.ListTrialsAsync(s.Id, ct));
    }
}
