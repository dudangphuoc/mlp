using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

// ───────── background runner (internal) ─────────

public sealed record ListOpenPrelabelJobsQuery : IQuery<List<OpenPrelabelJobRow>>;

public sealed class ListOpenPrelabelJobsQueryHandler(IReadSession db) : IQueryHandler<ListOpenPrelabelJobsQuery, List<OpenPrelabelJobRow>>
{
    public async ValueTask<List<OpenPrelabelJobRow>> Handle(ListOpenPrelabelJobsQuery q, CancellationToken ct) => await db.ListOpenPrelabelJobsAsync(ct);
}
