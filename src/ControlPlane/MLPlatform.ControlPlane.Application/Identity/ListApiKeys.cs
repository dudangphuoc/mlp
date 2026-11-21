using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>S-01-4: my API keys.</summary>
[Access(AccessKind.Authenticated)]
public sealed record ListApiKeysQuery : IQuery<IReadOnlyList<ApiKeyRow>>;

/// <summary>Handles <see cref="ListApiKeysQuery"/>.</summary>
public sealed class ListApiKeysQueryHandler(IReadSession db, ICurrentUser user) : IQueryHandler<ListApiKeysQuery, IReadOnlyList<ApiKeyRow>>
{
    public async ValueTask<IReadOnlyList<ApiKeyRow>> Handle(ListApiKeysQuery q, CancellationToken ct) => await db.ListApiKeysAsync(user.RequireUser(), ct);
}
