using System.Text.RegularExpressions;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

// ---- GetMe ----
public sealed record GetMeQuery : IQuery<MeDto>;

public sealed class GetMeQueryHandler(IReadSession db, ICurrentUser user) : IQueryHandler<GetMeQuery, MeDto>
{
    public async ValueTask<MeDto> Handle(GetMeQuery q, CancellationToken ct) =>
        await IdentityRules.LoadMe(db, user.RequireUser(), ct);
}
