using System.Security.Cryptography;
using System.Text;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Audit;

public sealed record AuditRow(long Seq, Guid Id, DateTimeOffset At, Guid? WorkspaceId, Guid? ActorId, string Action, string Outcome, string? Reason,
    string PrevHash, string Hash);

public static partial class AuditSql
{
    [SqlQuery("Audit/LockChain.sql")] public static partial Task LockChainAsync(this IWriteSession db, CancellationToken ct);
    [SqlQuery("Audit/LastHash.sql")] public static partial Task<string?> LastHashAsync(this IWriteSession db, CancellationToken ct);
    [SqlQuery("Audit/InsertEvent.sql")]
    public static partial Task InsertAuditEventAsync(this IWriteSession db, Guid id, DateTimeOffset at, Guid? workspaceId, Guid? actorId, string action,
        string outcome, string? reason, string prevHash, string hash, CancellationToken ct);
    [SqlQuery("Audit/ListEvents.sql")] public static partial Task<List<AuditRow>> ListAuditEventsAsync(this IReadSession db, Guid workspaceId, CancellationToken ct);
    [SqlQuery("Audit/AllEvents.sql")] public static partial Task<List<AuditRow>> AllEventsAsync(this IReadSession db, CancellationToken ct);
}

public static class AuditChain
{
    public static string Hash(string prev, Guid id, DateTimeOffset at, Guid? ws, Guid? actor, string action, string outcome, string? reason) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{prev}|{id}|{at.ToUnixTimeMilliseconds()}|{ws}|{actor}|{action}|{outcome}|{reason}")));

    public static async Task AppendAsync(IWriteSession db, IClock clock, Guid? ws, Guid? actor, string action, string outcome, string? reason, CancellationToken ct)
    {
        await db.LockChainAsync(ct);   // serialize the hash chain (held until the transaction ends)
        var prev = await db.LastHashAsync(ct) ?? "genesis";
        var id = Ids.New();
        var at = DateTimeOffset.FromUnixTimeMilliseconds(clock.UtcNow.ToUnixTimeMilliseconds());
        await db.InsertAuditEventAsync(id, at, ws, actor, action, outcome, reason, prev, Hash(prev, id, at, ws, actor, action, outcome, reason), ct);
    }
}

/// <summary>
/// Audits every user command (20/06 §6, M06): success is written in the same transaction; denied/failed is written in a separate transaction
/// (the main transaction has rolled back). Internal commands without a user (heartbeat, scheduler) are not recorded.
/// </summary>
public sealed class AuditBehavior(IWriteSession session, ICurrentUser user, IClock clock, DataSources sources) : IRequestBehavior
{
    /// <summary>Audited commands currently running in this call chain (1 = outermost).</summary>
    private static readonly AsyncLocal<int> Depth = new();

    public async ValueTask<object?> Handle(RequestContext ctx, Func<ValueTask<object?>> next, CancellationToken ct)
    {
        if (!ctx.IsCommand || ctx.Request is IInternalCommand) return await next();
        var depth = ++Depth.Value;
        try
        {
            var result = await next();
            if (user.UserId is not null && session.InTransaction)
                await AuditChain.AppendAsync(session, clock, ctx.WorkspaceId, user.UserId, ctx.Name, "ok", null, ct);
            return result;
        }
        catch (AppException e) when (user.UserId is not null || e is ForbiddenException or UnauthenticatedException)
        {
            var outcome = e is ForbiddenException or UnauthenticatedException ? "denied" : "failed";
            if (depth > 1 && session.InTransaction)
            {
                // A child command failed inside its parent's transaction: the parent may catch it and go on, so record it there.
                // (A separate connection would wait for the chain lock this very transaction may hold — a self-deadlock.)
                await AuditChain.AppendAsync(session, clock, ctx.WorkspaceId, user.UserId, ctx.Name, outcome, e.Reason, ct);
                throw;
            }
            // Outermost command: its work is void anyway. Release the transaction (and the chain lock a child command took) before
            // writing the failure separately; the unit of work's own rollback is then a no-op.
            await session.RollbackAsync(ct);
            await using var separate = new WriteSession(sources);
            await separate.BeginAsync(ct);
            await AuditChain.AppendAsync(separate, clock, ctx.WorkspaceId, user.UserId, ctx.Name, outcome, e.Reason, ct);
            await separate.CommitAsync(ct);
            throw;
        }
        finally { Depth.Value--; }
    }
}

public sealed record ListAuditQuery(Guid WorkspaceId) : IQuery<(IReadOnlyList<AuditRow> Events, bool ChainValid)>;

public sealed class ListAuditQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListAuditQuery, (IReadOnlyList<AuditRow> Events, bool ChainValid)>
{
    public async ValueTask<(IReadOnlyList<AuditRow> Events, bool ChainValid)> Handle(ListAuditQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "audit.read", ct);
        var all = await db.AllEventsAsync(ct);
        var prev = "genesis";
        var valid = true;
        foreach (var e in all)
        {
            if (e.PrevHash != prev || e.Hash != AuditChain.Hash(e.PrevHash, e.Id, e.At, e.WorkspaceId, e.ActorId, e.Action, e.Outcome, e.Reason)) { valid = false; break; }
            prev = e.Hash;
        }
        return (await db.ListAuditEventsAsync(q.WorkspaceId, ct), valid);
    }
}
