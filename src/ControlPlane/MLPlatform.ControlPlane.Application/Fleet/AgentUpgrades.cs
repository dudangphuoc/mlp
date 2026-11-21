using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

/// <summary>One agent upgrade (D-38).</summary>
public sealed record AgentUpgradeRow(Guid Id, Guid HostId, string HostName, string FromVersion, string ToVersion, string State, string Phase,
    string Message, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt);
/// <summary>Unfinished upgrade job (resumed after the api restarts).</summary>
public sealed record ActiveAgentUpgradeRow(Guid Id, Guid WorkspaceId, Guid HostId, string ToVersion, string State);

/// <summary>Agent upgrade history queries.</summary>
public static partial class AgentUpgradeSql
{
    [SqlQuery("Fleet/InsertAgentUpgrade.sql")]
    public static partial Task InsertAgentUpgradeAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid hostId, string fromVersion, string toVersion,
        string state, string message, Guid? createdBy, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Fleet/UpdateAgentUpgrade.sql")]
    public static partial Task UpdateAgentUpgradeAsync(this IWriteSession db, Guid id, string state, string phase, string message, DateTimeOffset now, CancellationToken ct);
    [SqlQuery("Fleet/ListAgentUpgrades.sql")]
    public static partial Task<List<AgentUpgradeRow>> ListAgentUpgradesAsync(this IReadSession db, Guid workspaceId, Guid? hostId, CancellationToken ct);
    [SqlQuery("Fleet/ActiveAgentUpgrades.sql")]
    public static partial Task<List<ActiveAgentUpgradeRow>> ActiveAgentUpgradesAsync(this IReadSession db, CancellationToken ct);
}

/// <summary>A user requests an upgrade of one machine: checks permission, records the job (one running job per machine). Returns the job id, or null if a job is already in progress.</summary>
public sealed record RequestAgentUpgradeCommand(Guid WorkspaceId, Guid HostId, string FromVersion, string ToVersion, bool Skip, string Message) : ICommand<Guid>;

/// <summary>Handles <see cref="RequestAgentUpgradeCommand"/>.</summary>
public sealed class RequestAgentUpgradeCommandHandler(IWriteSession db, IClock clock, WorkspaceAuthorizer auth) : ICommandHandler<RequestAgentUpgradeCommand, Guid>
{
    public async ValueTask<Guid> Handle(RequestAgentUpgradeCommand c, CancellationToken ct)
    {
        var uid = await auth.RequireAsync(c.WorkspaceId, "fleet.manage", ct);
        var id = Ids.New();
        await db.InsertAgentUpgradeAsync(id, c.WorkspaceId, c.HostId, c.FromVersion, c.ToVersion, c.Skip ? "skipped" : "queued", c.Message, uid, clock.UtcNow, ct);
        return id;
    }
}

/// <summary>Orchestration loop updates job state (internal, individual steps are not audited).</summary>
public sealed record UpdateAgentUpgradeCommand(Guid Id, string State, string Phase, string Message) : ICommand<bool>, IInternalCommand;

/// <summary>Handles <see cref="UpdateAgentUpgradeCommand"/>.</summary>
public sealed class UpdateAgentUpgradeCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<UpdateAgentUpgradeCommand, bool>
{
    public async ValueTask<bool> Handle(UpdateAgentUpgradeCommand c, CancellationToken ct)
    {
        await db.UpdateAgentUpgradeAsync(c.Id, c.State, c.Phase, c.Message, clock.UtcNow, ct);
        return true;
    }
}

/// <summary>Agent upgrade history of a workspace (filterable by machine).</summary>
public sealed record ListAgentUpgradesQuery(Guid WorkspaceId, Guid? HostId) : IQuery<IReadOnlyList<AgentUpgradeRow>>;

/// <summary>Handles <see cref="ListAgentUpgradesQuery"/>.</summary>
public sealed class ListAgentUpgradesQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListAgentUpgradesQuery, IReadOnlyList<AgentUpgradeRow>>
{
    public async ValueTask<IReadOnlyList<AgentUpgradeRow>> Handle(ListAgentUpgradesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        return await db.ListAgentUpgradesAsync(q.WorkspaceId, q.HostId, ct);
    }
}

/// <summary>In-progress upgrade jobs across the instance.</summary>
public sealed record ActiveAgentUpgradesQuery : IQuery<IReadOnlyList<ActiveAgentUpgradeRow>>;

/// <summary>Handles <see cref="ActiveAgentUpgradesQuery"/>.</summary>
public sealed class ActiveAgentUpgradesQueryHandler(IReadSession db) : IQueryHandler<ActiveAgentUpgradesQuery, IReadOnlyList<ActiveAgentUpgradeRow>>
{
    public async ValueTask<IReadOnlyList<ActiveAgentUpgradeRow>> Handle(ActiveAgentUpgradesQuery q, CancellationToken ct) => await db.ActiveAgentUpgradesAsync(ct);
}
