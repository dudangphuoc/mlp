using MLPlatform.ControlPlane.Application.Fleet;
namespace MLPlatform.ControlPlane.Application.Ops.Gpu;

/// <summary>Everything the GPU view of one machine needs; MetricsJson is the agent's last HostMetrics.</summary>
public sealed record HostGpuData(HostRow Host, string? MetricsJson, DateTimeOffset? MetricsAt, IReadOnlyList<GpuAlertRow> Alerts,
    IReadOnlyList<GpuReservation> Reservations);
