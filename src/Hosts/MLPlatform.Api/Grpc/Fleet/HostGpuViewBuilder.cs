using Google.Protobuf.WellKnownTypes;
using MLPlatform.ControlPlane.Application.Ops.Gpu;
using MLPlatform.Contracts.V1;

namespace MLPlatform.Api.Grpc;

/// <summary>Builds the GPU view of a machine: cards with live readings, reservations, alerts, and who uses each
/// GPU.</summary>
public static class HostGpuViewBuilder
{
    private static readonly Google.Protobuf.JsonParser Parser = new(Google.Protobuf.JsonParser.Settings.Default.WithIgnoreUnknownFields(true));

    public static HostGpuView Build(HostGpuData d)
    {
        var info = d.Host.HostInfo is { Length: > 0 } b ? HostInfo.Parser.ParseFrom(b) : new HostInfo();
        var hm = d.MetricsJson is { Length: > 0 } json ? Parser.Parse<HostMetrics>(json) : new HostMetrics();
        var view = new HostGpuView
        {
            State = hm.GpuState == GpuState.Unspecified ? (info.Gpus.Count > 0 ? GpuState.Ok : GpuState.None) : hm.GpuState,
            Error = hm.GpuError, Yield = hm.GpuYield ?? new GpuYield(), GpuPriority = info.GpuPriority,
        };
        if (d.MetricsAt is { } at) view.SampledAt = Timestamp.FromDateTimeOffset(at);
        foreach (var g in info.Gpus.OrderBy(g => g.Index)) view.Gpus.Add(Card(g, hm, d));
        view.Usage.AddRange(Usage(hm, d));
        return view;
    }

    private static GpuCard Card(GpuInfo g, HostMetrics hm, HostGpuData d)
    {
        var sample = hm.Gpus.FirstOrDefault(s => s.Uuid == g.Uuid);
        var total = sample?.MemoryTotalBytes is > 0 ? sample.MemoryTotalBytes : g.MemoryBytes;
        var reservations = d.Reservations.Where(r => r.GpuUuid == g.Uuid).ToList();
        var reserved = reservations.Any(r => r.ReservedMb <= 0) ? total : (ulong)reservations.Sum(r => r.ReservedMb) << 20;
        var used = sample?.MemoryUsedBytes ?? 0;
        var card = new GpuCard
        {
            Info = g, Sample = sample ?? new GpuSample { Uuid = g.Uuid, Index = g.Index },
            ReservedMemoryBytes = Math.Min(reserved, total),
            SchedulableMemoryBytes = total > Math.Max(used, reserved) ? total - Math.Max(used, reserved) : 0,
        };
        card.Alerts.AddRange(d.Alerts.Where(a => a.GpuUuid == g.Uuid || a.GpuUuid == GpuAlertRules.HostWide).Select(Alert));
        return card;
    }

    private static GpuAlert Alert(GpuAlertRow a) => new() { GpuUuid = a.GpuUuid, Kind = a.Kind, Level = a.Level, Since = Timestamp.FromDateTimeOffset(a.Since) };

    private static IEnumerable<GpuUsage> Usage(HostMetrics hm, HostGpuData d)
    {
        var self = d.Host.Id.ToString();
        foreach (var p in hm.GpuProcesses.Where(p => p.Container.Length > 0 || p.UsedMemoryBytes > 0))
        {
            var job = Guid.TryParse(p.AttemptId, out var aid) ? d.Reservations.FirstOrDefault(r => r.AttemptId == aid) : null;
            yield return new GpuUsage
            {
                GpuUuid = p.GpuUuid, Container = p.Container, AttemptId = p.AttemptId, UsedMemoryBytes = p.UsedMemoryBytes, Estimated = p.Estimated,
                JobId = job?.JobId.ToString() ?? "", JobName = job?.JobName ?? "",
                Environment = p.OwnerAgent.Length > 0 && p.OwnerAgent != self ? "other" : "this",
            };
        }
    }
}
