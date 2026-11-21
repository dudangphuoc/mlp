using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>Query that checks the enroll token for the install flow (the script sends doctor results before it has a credential).</summary>
public static partial class AgentBuildSql
{
    [SqlQuery("Fleet/EnrollTokenValid.sql")]
    public static partial Task<long> EnrollTokenValidAsync(this IReadSession db, string tokenHash, DateTimeOffset now, CancellationToken ct);
}

/// <summary>Enroll token is still usable (unused, not expired) — the entry point of <c>/install/agent/build</c>.</summary>
public sealed record EnrollTokenValidQuery(string Token) : IQuery<bool>;

/// <summary>Handles <see cref="EnrollTokenValidQuery"/>.</summary>
public sealed class EnrollTokenValidQueryHandler(IReadSession db, IClock clock, TokenService tokens) : IQueryHandler<EnrollTokenValidQuery, bool>
{
    public async ValueTask<bool> Handle(EnrollTokenValidQuery q, CancellationToken ct) =>
        q.Token.StartsWith("mlpe_", StringComparison.Ordinal) && await db.EnrollTokenValidAsync(tokens.Hash(q.Token), clock.UtcNow, ct) > 0;
}
