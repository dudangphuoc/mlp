using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

/// <summary>Internal (after the RPC checked the permission): the recorded environments of a recipe in a
/// workspace.</summary>
[Access(AccessKind.Internal)]
public sealed record ListEnvironmentRowsQuery(Guid WorkspaceId, string RecipeName) : IQuery<IReadOnlyList<EnvironmentRow>>;

/// <summary>A build that has been running longer than the limit was lost with an api restart and reads as
/// failed.</summary>
public sealed class ListEnvironmentRowsQueryHandler(IReadSession db, IClock clock) : IQueryHandler<ListEnvironmentRowsQuery, IReadOnlyList<EnvironmentRow>>
{
    public async ValueTask<IReadOnlyList<EnvironmentRow>> Handle(ListEnvironmentRowsQuery q, CancellationToken ct) =>
        [.. (await db.ListEnvironmentsAsync(q.WorkspaceId, q.RecipeName, ct)).Select(e => Settle(e, clock.UtcNow))];

    public static EnvironmentRow Settle(EnvironmentRow e, DateTimeOffset now) =>
        e.State == "building" && now - e.StartedAt > InspectionRules.BuildLimit ? e with { State = "failed", Error = "interrupted" } : e;
}
