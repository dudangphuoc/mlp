using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ops;

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
