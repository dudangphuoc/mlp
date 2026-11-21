using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Enroll token is still usable (unused, not expired) — the entry point of /install/agent/build .</summary>
[Access(AccessKind.Internal)]
public sealed record EnrollTokenValidQuery(string Token) : IQuery<bool>;

/// <summary>Handles <see cref="EnrollTokenValidQuery"/>.</summary>
public sealed class EnrollTokenValidQueryHandler(IReadSession db, IClock clock, TokenService tokens) : IQueryHandler<EnrollTokenValidQuery, bool>
{
    public async ValueTask<bool> Handle(EnrollTokenValidQuery q, CancellationToken ct) =>
        q.Token.StartsWith("mlpe_", StringComparison.Ordinal) && await db.EnrollTokenValidAsync(tokens.Hash(q.Token), clock.UtcNow, ct) > 0;
}
