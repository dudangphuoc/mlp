using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

/// <summary>Estimate for every machine that has a cache of the dataset (machines without one would download
/// everything).</summary>
[RequiresPermission("run.read")]
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
