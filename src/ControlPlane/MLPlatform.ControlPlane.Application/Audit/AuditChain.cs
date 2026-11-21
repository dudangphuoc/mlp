using System.Security.Cryptography;
using System.Text;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Audit;

public static class AuditChain
{
    public static string Hash(string prev, Guid id, DateTimeOffset at, Guid? ws, Guid? actor, string action, string outcome, string? reason) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{prev}|{id}|{at.ToUnixTimeMilliseconds()}|{ws}|{actor}|{action}|{outcome}|{reason}")));

    public static async Task AppendAsync(IWriteSession db, IClock clock, Guid? ws, Guid? actor, string action, string outcome, string? reason, CancellationToken ct)
    {
        await db.LockChainAsync(ct);
        var prev = await db.LastHashAsync(ct) ?? "genesis";
        var id = Ids.New();
        var at = DateTimeOffset.FromUnixTimeMilliseconds(clock.UtcNow.ToUnixTimeMilliseconds());
        await db.InsertAuditEventAsync(id, at, ws, actor, action, outcome, reason, prev, Hash(prev, id, at, ws, actor, action, outcome, reason), ct);
    }
}
