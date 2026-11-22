using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MLPlatform.ControlPlane.Application.Ops.Gpu;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Api.Grpc;

/// <summary>GPU monitoring RPCs (docs/agent-upgrade.md §2).</summary>
public sealed partial class FleetGrpc
{
    public override async Task<HostGpuView> GetHostGpus(GetHostRequest r, ServerCallContext ctx) =>
        HostGpuViewBuilder.Build(await mediator.Query(new GetHostGpusQuery(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id")), ctx.CancellationToken));

    public override async Task<GpuHistory> GetGpuHistory(GetGpuHistoryRequest r, ServerCallContext ctx)
    {
        var d = await mediator.Query(new GetGpuHistoryQuery(G(r.WorkspaceId, "workspace_id"), G(r.HostId, "host_id"), r.GpuUuid, r.Range), ctx.CancellationToken);
        var res = new GpuHistory { StepSeconds = d.StepSeconds, MemoryTotalBytes = (ulong)(d.Points.Count > 0 ? d.Points.Max(p => p.VramTotal) : 0) };
        res.Points.AddRange(d.Points.Select(p => new GpuPoint
        {
            At = Timestamp.FromDateTimeOffset(p.At), UtilPercent = p.Util, MemoryUsedBytes = (ulong)p.VramUsed, TemperatureC = p.Temp, PowerW = p.Power,
            SmClockMhz = p.SmClock ?? 0,
        }));
        res.Jobs.AddRange(d.Jobs.Select(j => new GpuJobMark
        {
            JobId = j.JobId.ToString(), JobName = j.JobName,
            StartedAt = j.StartedAt is { } s ? Timestamp.FromDateTimeOffset(s) : null, FinishedAt = j.FinishedAt is { } f ? Timestamp.FromDateTimeOffset(f) : null,
        }));
        return res;
    }
}
