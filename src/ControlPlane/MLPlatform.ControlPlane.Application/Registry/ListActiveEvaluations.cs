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

// ───────────── Engine (EvaluationRunner in the api) ─────────────

public sealed record ListActiveEvaluationsQuery : IQuery<IReadOnlyList<EvaluationRow>>;

public sealed class ListActiveEvaluationsQueryHandler(IReadSession db) : IQueryHandler<ListActiveEvaluationsQuery, IReadOnlyList<EvaluationRow>>
{
    public async ValueTask<IReadOnlyList<EvaluationRow>> Handle(ListActiveEvaluationsQuery q, CancellationToken ct) => await db.ListActiveEvaluationsAsync(ct);
}
