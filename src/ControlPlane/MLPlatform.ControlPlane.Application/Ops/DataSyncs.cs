using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>One finished dataset sync of an attempt into a host's dataset cache (what was reused, what was downloaded, how long).</summary>
public sealed record DataSyncRow(Guid Id, Guid JobId, Guid AttemptId, Guid HostId, Guid DatasetId, int Files, int ReusedFiles, long ReusedBytes,
    int FetchedFiles, long FetchedBytes, int UncachedFiles, int Retries, double Seconds, long CacheFiles, long CacheBytes, long FreeBytes, DateTimeOffset At);

/// <summary>A dataset cache volume a machine holds (latest sync per host and dataset).</summary>
public sealed record DataCacheRow(Guid HostId, Guid DatasetId, long CacheFiles, long CacheBytes, long FreeBytes, DateTimeOffset LastUsedAt, Guid LastJobId)
{
    public bool InUse { get; init; }

    /// <summary>Marks rows whose last job is still active (job states come from the scheduler module).</summary>
    public static async Task<List<DataCacheRow>> WithUseAsync(IMediator mediator, List<DataCacheRow> rows, CancellationToken ct)
    {
        var res = new List<DataCacheRow>(rows.Count);
        foreach (var r in rows)
            res.Add(r with { InUse = await mediator.Query(new MLPlatform.ControlPlane.Application.Scheduling.JobStateQuery(r.LastJobId), ct)
                is { } s && s is not ("succeeded" or "failed" or "cancelled" or "stopped") });
        return res;
    }
}

/// <summary>SQL of the host dataset cache records (ops.data_syncs).</summary>
public static partial class DataSyncSql
{
    [SqlQuery("Ops/InsertDataSync.sql")]
    public static partial Task InsertDataSyncAsync(this IWriteSession db, Guid id, Guid workspaceId, Guid jobId, Guid attemptId, Guid hostId, Guid datasetId,
        int files, int reusedFiles, long reusedBytes, int fetchedFiles, long fetchedBytes, int uncachedFiles, int retries, double seconds,
        long cacheFiles, long cacheBytes, long freeBytes, DateTimeOffset at, Guid? versionId, bool subset, CancellationToken ct);
    [SqlQuery("Ops/SyncedVersionsOnHosts.sql")]
    public static partial Task<List<SyncedVersionRow>> SyncedVersionsOnHostsAsync(this IReadSession db, Guid workspaceId, Guid datasetId, CancellationToken ct);
    [SqlQuery("Ops/ListJobDataSyncs.sql")]
    public static partial Task<List<DataSyncRow>> ListJobDataSyncsAsync(this IReadSession db, Guid workspaceId, Guid jobId, CancellationToken ct);
    [SqlQuery("Ops/ListHostDataCaches.sql")]
    public static partial Task<List<DataCacheRow>> ListHostDataCachesAsync(this IReadSession db, Guid workspaceId, Guid? hostId, CancellationToken ct);
    [SqlQuery("Ops/DatasetCacheByHost.sql")]
    public static partial Task<List<HostCacheRow>> DatasetCacheByHostAsync(this IReadSession db, Guid workspaceId, Guid datasetId, CancellationToken ct);
    [SqlQuery("Ops/ListAllDataCaches.sql")]
    public static partial Task<List<JanitorCacheRow>> ListAllDataCachesAsync(this IReadSession db, CancellationToken ct);
    [SqlQuery("Ops/MarkDataCacheRemoved.sql")]
    public static partial Task MarkDataCacheRemovedAsync(this IWriteSession db, Guid workspaceId, Guid hostId, Guid datasetId, DateTimeOffset now, CancellationToken ct);
}

/// <summary>Keeps the shim's final sync report (progress detail <c>sync</c> with <c>final: true</c>) — the live progress is overwritten by later phases.</summary>
public static class DataSyncRecorder
{
    internal static string Size(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.00} GB" : b >= 1L << 20 ? $"{b / (double)(1L << 20):0.0} MB"
        : b >= 1024 ? $"{b / 1024.0:0} KB" : $"{b} B";

    public static async Task RecordAsync(IWriteSession db, IClock clock, WorkloadAttemptRow a, JsonObject sync, CancellationToken ct)
    {
        if (sync["final"]?.GetValue<bool>() != true || a.HostIds.Length == 0 || !Guid.TryParse(sync["dataset"]?.ToString(), out var datasetId)) return;
        long L(string k) => sync[k] is JsonValue v && v.TryGetValue<long>(out var x) ? x : 0;
        var failed = (sync["failed"] as JsonArray)?.Count ?? 0;
        await db.InsertDataSyncAsync(Ids.New(), a.WorkspaceId, a.JobId, a.Id, a.HostIds[0], datasetId, (int)L("files"), (int)L("reused_files"), L("reused_bytes"),
            (int)L("done_files"), L("done_bytes"), (int)L("uncached_files"), (int)L("retries"), sync["seconds"] is JsonValue s && s.TryGetValue<double>(out var sec) ? sec : 0,
            L("cache_files"), L("cache_bytes"), L("free_bytes"), clock.UtcNow,
            Guid.TryParse(sync["version"]?.ToString(), out var vid) ? vid : null, sync["subset"]?.GetValue<bool>() == true, ct);
        await TimelineEvents.EmitAsync(db, clock, a.WorkspaceId, "shim", "attempt", a.Id, "data.synced", failed > 0 ? "warning" : "success",
            $"Dataset: {L("files")} files · {L("reused_files")} reused ({Size(L("reused_bytes"))}) · {L("done_files")} downloaded ({Size(L("done_bytes"))}) · {sync["seconds"]} s",
            sync.DeepClone().AsObject(), ct: ct);
    }
}

/// <summary>A cache volume as the janitor sees it (any workspace).</summary>
public sealed record JanitorCacheRow(Guid WorkspaceId, Guid HostId, Guid DatasetId, long CacheBytes, long FreeBytes, DateTimeOffset LastUsedAt, Guid LastJobId);

/// <summary>Automatic clean-up rules of host dataset caches.</summary>
public static class DataCacheJanitorRules
{
    public static readonly TimeSpan MaxIdle = TimeSpan.FromDays(14);
    public const long MinFreeBytes = 50L << 30;
}

/// <summary>
/// Picks the cache volumes to delete: idle longer than <see cref="DataCacheJanitorRules.MaxIdle"/>, plus — on a machine whose free disk is below
/// <see cref="DataCacheJanitorRules.MinFreeBytes"/> — idle caches oldest first until enough is freed. Caches whose last job is active are never picked.
/// </summary>
public sealed record PlanDataCacheCleanupCommand : ICommand<IReadOnlyList<(JanitorCacheRow Cache, string Reason)>>, IInternalCommand;

/// <summary>Computes the clean-up plan from the stored sync reports.</summary>
public sealed class PlanDataCacheCleanupCommandHandler(IReadSession db, IClock clock, IMediator mediator)
    : ICommandHandler<PlanDataCacheCleanupCommand, IReadOnlyList<(JanitorCacheRow Cache, string Reason)>>
{
    public async ValueTask<IReadOnlyList<(JanitorCacheRow Cache, string Reason)>> Handle(PlanDataCacheCleanupCommand c, CancellationToken ct)
    {
        var plan = new List<(JanitorCacheRow, string)>();
        var now = clock.UtcNow;
        foreach (var host in (await db.ListAllDataCachesAsync(ct)).GroupBy(r => r.HostId))
        {
            var idle = new List<JanitorCacheRow>();
            foreach (var r in host)   // oldest use first
                if (await mediator.Query(new MLPlatform.ControlPlane.Application.Scheduling.JobStateQuery(r.LastJobId), ct) is null or "succeeded" or "failed" or "cancelled" or "stopped")
                    idle.Add(r);
            var free = host.MaxBy(r => r.LastUsedAt)!.FreeBytes;   // latest report of the machine
            foreach (var r in idle)
            {
                if (now - r.LastUsedAt > DataCacheJanitorRules.MaxIdle) { plan.Add((r, $"unused for {(int)(now - r.LastUsedAt).TotalDays} days")); free += r.CacheBytes; }
                else if (free < DataCacheJanitorRules.MinFreeBytes) { plan.Add((r, $"disk low ({DataSyncRecorder.Size(free)} free)")); free += r.CacheBytes; }
            }
        }
        return plan;
    }
}

/// <summary>Records one automatic removal (sync rows marked removed + event on the host).</summary>
public sealed record RecordDataCacheCleanupCommand(JanitorCacheRow Cache, string Reason) : ICommand<bool>, IInternalCommand;

/// <summary>Marks the cache removed and emits `data.cache_removed`.</summary>
public sealed class RecordDataCacheCleanupCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<RecordDataCacheCleanupCommand, bool>
{
    public async ValueTask<bool> Handle(RecordDataCacheCleanupCommand c, CancellationToken ct)
    {
        var r = c.Cache;
        await db.MarkDataCacheRemovedAsync(r.WorkspaceId, r.HostId, r.DatasetId, clock.UtcNow, ct);
        await TimelineEvents.EmitAsync(db, clock, r.WorkspaceId, "system:janitor", "host", r.HostId, "data.cache_removed", "info",
            $"Dataset cache removed ({DataSyncRecorder.Size(r.CacheBytes)}): {c.Reason}",
            new JsonObject { ["dataset_id"] = r.DatasetId.ToString(), ["bytes"] = r.CacheBytes, ["reason"] = c.Reason }, ct: ct);
        return true;
    }
}

/// <summary>A whole dataset version a machine synced into its (still existing) cache.</summary>
public sealed record SyncedVersionRow(Guid HostId, Guid VersionId);

/// <summary>Before a run: per machine, how much of a dataset version is already there and how much would be downloaded.</summary>
public sealed record HostDataEstimate(Guid HostId, long Files, long Bytes, long CachedFiles, long CachedBytes, long FreeBytes, bool Known);

/// <summary>
/// Estimate for every machine that has a cache of the dataset (machines without one would download everything). Cached = files of the version
/// whose cache id belongs to a whole version previously synced on that machine. Trial samples are not counted, so the estimate errs on the high side.
/// </summary>
public sealed record EstimateDataTransferQuery(Guid WorkspaceId, Guid VersionId) : IQuery<(long Files, long Bytes, IReadOnlyList<HostDataEstimate> Hosts)>;

/// <summary>Computes the per-machine transfer estimate from sync history and the versions' file ids.</summary>
public sealed class EstimateDataTransferQueryHandler(IReadSession db, IMediator mediator, WorkspaceAuthorizer auth)
    : IQueryHandler<EstimateDataTransferQuery, (long Files, long Bytes, IReadOnlyList<HostDataEstimate> Hosts)>
{
    public async ValueTask<(long Files, long Bytes, IReadOnlyList<HostDataEstimate> Hosts)> Handle(EstimateDataTransferQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        var (datasetId, files) = await mediator.Query(new MLPlatform.ControlPlane.Application.Data.VersionFileIdsQuery(q.WorkspaceId, q.VersionId), ct);
        long total = files.Count, bytes = files.Sum(f => f.Size);
        var synced = await db.SyncedVersionsOnHostsAsync(q.WorkspaceId, datasetId, ct);
        var caches = (await db.ListHostDataCachesAsync(q.WorkspaceId, null, ct)).Where(c => c.DatasetId == datasetId).ToDictionary(c => c.HostId);
        var idsOf = new Dictionary<Guid, HashSet<string>>();
        var res = new List<HostDataEstimate>();
        foreach (var (host, cache) in caches)
        {
            var have = new HashSet<string>();
            foreach (var v in synced.Where(s => s.HostId == host).Select(s => s.VersionId))
            {
                if (!idsOf.TryGetValue(v, out var ids))
                    idsOf[v] = ids = (await mediator.Query(new MLPlatform.ControlPlane.Application.Data.VersionFileIdsQuery(q.WorkspaceId, v), ct)).Files
                        .Select(f => f.Id).OfType<string>().ToHashSet();
                have.UnionWith(ids);
            }
            var hit = files.Where(f => f.Id is not null && have.Contains(f.Id)).ToList();
            res.Add(new HostDataEstimate(host, total, bytes, hit.Count, hit.Sum(f => f.Size), cache.FreeBytes, synced.Any(s => s.HostId == host)));
        }
        return (total, bytes, res.OrderByDescending(r => r.CachedBytes).ToList());
    }
}

/// <summary>How much of one dataset a machine holds in its cache.</summary>
public sealed record HostCacheRow(Guid HostId, long CacheBytes, long CacheFiles);

/// <summary>
/// Scheduler: orders the machines that fit a job so the one already holding most of the job's dataset comes first (the data does not
/// travel again), and records the choice on the job's timeline. Machines without the data keep their original order after it.
/// </summary>
public sealed record PreferDataHostsCommand(Guid WorkspaceId, Guid JobId, Guid DatasetId, IReadOnlyList<Guid> Candidates) : ICommand<IReadOnlyList<Guid>>, IInternalCommand;

/// <summary>Sorts candidate hosts by cached bytes of the dataset and emits a `data.placement` event.</summary>
public sealed class PreferDataHostsCommandHandler(IWriteSession db, IClock clock) : ICommandHandler<PreferDataHostsCommand, IReadOnlyList<Guid>>
{
    public async ValueTask<IReadOnlyList<Guid>> Handle(PreferDataHostsCommand c, CancellationToken ct)
    {
        var held = (await db.DatasetCacheByHostAsync(c.WorkspaceId, c.DatasetId, ct)).ToDictionary(r => r.HostId);
        var ordered = c.Candidates.Select((h, i) => (h, i)).OrderByDescending(x => held.TryGetValue(x.h, out var r) ? r.CacheBytes : -1).ThenBy(x => x.i)
            .Select(x => x.h).ToList();
        if (held.Count > 0)
        {
            var first = ordered[0];
            var hit = held.TryGetValue(first, out var r);
            await TimelineEvents.EmitAsync(db, clock, c.WorkspaceId, "scheduler", "job", c.JobId, "data.placement", hit ? "info" : "warning",
                hit ? $"Placed on a machine that already holds this dataset ({r!.CacheFiles} files, {DataSyncRecorder.Size(r.CacheBytes)})"
                    : "The machines holding this dataset cannot take the job now — the dataset is downloaded on another machine",
                new System.Text.Json.Nodes.JsonObject { ["host_id"] = first.ToString(), ["cached_bytes"] = hit ? r!.CacheBytes : 0 }, ct: ct);
        }
        return ordered;
    }
}

/// <summary>Dataset syncs of a job (newest first) for the job page.</summary>
public sealed record ListJobDataSyncsQuery(Guid WorkspaceId, Guid JobId) : IQuery<IReadOnlyList<DataSyncRow>>;

/// <summary>Reads the stored dataset syncs of a job.</summary>
public sealed class ListJobDataSyncsQueryHandler(IReadSession db, WorkspaceAuthorizer auth) : IQueryHandler<ListJobDataSyncsQuery, IReadOnlyList<DataSyncRow>>
{
    public async ValueTask<IReadOnlyList<DataSyncRow>> Handle(ListJobDataSyncsQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "run.read", ct);
        return await db.ListJobDataSyncsAsync(q.WorkspaceId, q.JobId, ct);
    }
}

/// <summary>Dataset caches held by machines (one host, or all when HostId is null).</summary>
public sealed record ListDataCachesQuery(Guid WorkspaceId, Guid? HostId) : IQuery<IReadOnlyList<DataCacheRow>>;

/// <summary>Lists the dataset cache volumes known from sync reports.</summary>
public sealed class ListDataCachesQueryHandler(IReadSession db, IMediator mediator, WorkspaceAuthorizer auth) : IQueryHandler<ListDataCachesQuery, IReadOnlyList<DataCacheRow>>
{
    public async ValueTask<IReadOnlyList<DataCacheRow>> Handle(ListDataCachesQuery q, CancellationToken ct)
    {
        await auth.RequireAsync(q.WorkspaceId, "fleet.read", ct);
        return await DataCacheRow.WithUseAsync(mediator, await db.ListHostDataCachesAsync(q.WorkspaceId, q.HostId, ct), ct);
    }
}

/// <summary>
/// Deletes a machine's cache of one dataset (also how a full re-download is forced). Refused while a job using it is active; the caller sends
/// the volume removal to the agent after this succeeds.
/// </summary>
public sealed record DeleteDataCacheCommand(Guid WorkspaceId, Guid HostId, Guid DatasetId) : ICommand<string>;

/// <summary>Checks the cache is idle, marks it removed and returns the volume name to remove on the host.</summary>
public sealed class DeleteDataCacheCommandHandler(IWriteSession db, IClock clock, IMediator mediator, WorkspaceAuthorizer auth) : ICommandHandler<DeleteDataCacheCommand, string>
{
    public async ValueTask<string> Handle(DeleteDataCacheCommand c, CancellationToken ct)
    {
        await auth.RequireAsync(c.WorkspaceId, "fleet.manage", ct);
        var row = (await DataCacheRow.WithUseAsync(mediator, await db.ListHostDataCachesAsync(c.WorkspaceId, c.HostId, ct), ct)).FirstOrDefault(r => r.DatasetId == c.DatasetId)
            ?? throw new NotFoundException("FLEET_DATA_CACHE_NOT_FOUND", "This machine holds no cache of that dataset");
        if (row.InUse) throw new PreconditionException("FLEET_DATA_CACHE_IN_USE", "A job using this dataset is still active on this machine");
        await db.MarkDataCacheRemovedAsync(c.WorkspaceId, c.HostId, c.DatasetId, clock.UtcNow, ct);
        return MLPlatform.ControlPlane.Application.Data.DataCacheRules.Volume(c.DatasetId);
    }
}
