using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Serving;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.LlmOps;

public sealed record GetLlmUsageQuery(Guid WorkspaceId, int Days) : IQuery<IReadOnlyList<UsageDayRow>>;

public sealed class GetLlmUsageQueryHandler(IReadSession db, IClock clock, WorkspaceAuthorizer auth) : IQueryHandler<GetLlmUsageQuery, IReadOnlyList<UsageDayRow>>
{
    public async ValueTask<IReadOnlyList<UsageDayRow>> Handle(GetLlmUsageQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.UsageByDayAsync(q.WorkspaceId, clock.UtcNow.AddDays(-(q.Days is > 0 and <= 365 ? q.Days : 30)), ct);
    }
}
