using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Recipes;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Catalog;

[Access(AccessKind.Internal)]
public sealed record ListTasksQuery : IQuery<IReadOnlyList<TaskRow>>;

public sealed class ListTasksQueryHandler(IReadSession db) : IQueryHandler<ListTasksQuery, IReadOnlyList<TaskRow>>
{
    public async ValueTask<IReadOnlyList<TaskRow>> Handle(ListTasksQuery q, CancellationToken ct) => await db.ListTasksAsync(ct);
}
