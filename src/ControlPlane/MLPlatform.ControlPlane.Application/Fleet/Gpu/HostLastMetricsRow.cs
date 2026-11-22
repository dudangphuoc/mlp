namespace MLPlatform.ControlPlane.Application.Fleet.Gpu;

public sealed record HostLastMetricsRow(string? Metrics, DateTimeOffset? MetricsAt);
