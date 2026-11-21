using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Catalog;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Labeling;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>One run as the run page shows it.</summary>
public sealed record GetRunViewQuery(Guid WorkspaceId, Guid RunId) : IQuery<RunView>;

/// <summary>Reads one run and attaches job, dataset and user details.</summary>
public sealed class GetRunViewQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<GetRunViewQuery, RunView>
{
    public async ValueTask<RunView> Handle(GetRunViewQuery q, CancellationToken ct)
    {
        var r = await RunRules.RequireAsync(db, auth, q.WorkspaceId, q.RunId, "run.read", ct);
        return (await RunViews.BuildAsync(db, [r], ct))[0];
    }
}
