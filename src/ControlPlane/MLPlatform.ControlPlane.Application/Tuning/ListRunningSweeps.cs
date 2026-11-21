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

public sealed record ListRunningSweepsQuery : IQuery<IReadOnlyList<SweepRow>>;

public sealed class ListRunningSweepsQueryHandler(IReadSession db) : IQueryHandler<ListRunningSweepsQuery, IReadOnlyList<SweepRow>>
{
    public async ValueTask<IReadOnlyList<SweepRow>> Handle(ListRunningSweepsQuery q, CancellationToken ct) => await db.ListRunningSweepsAsync(ct);
}
