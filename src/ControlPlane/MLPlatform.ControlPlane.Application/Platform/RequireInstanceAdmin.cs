using MLPlatform.Cqrs;

namespace MLPlatform.ControlPlane.Application.Platform;

/// <summary>Checks that the caller is an Instance Admin, for work done outside the handlers (catalog sync).</summary>
[Access(AccessKind.Internal)]
public sealed record RequireInstanceAdminQuery : IQuery<bool>;

public sealed class RequireInstanceAdminQueryHandler(InstanceAdminGuard guard) : IQueryHandler<RequireInstanceAdminQuery, bool>
{
    public async ValueTask<bool> Handle(RequireInstanceAdminQuery q, CancellationToken ct)
    {
        await guard.RequireAsync(ct);
        return true;
    }
}
