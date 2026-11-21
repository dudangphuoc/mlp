using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

// ---- First-time setup (D-27, M01 R7) ----
public sealed record GetSetupStateQuery : IQuery<bool>;

public sealed class GetSetupStateQueryHandler(IReadSession db) : IQueryHandler<GetSetupStateQuery, bool>
{
    public async ValueTask<bool> Handle(GetSetupStateQuery q, CancellationToken ct) =>
        (await db.GetInstanceSettingsAsync(ct))?.BootstrappedAt is null;
}
