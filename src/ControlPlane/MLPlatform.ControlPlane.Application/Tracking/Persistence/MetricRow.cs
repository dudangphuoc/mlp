using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

public sealed record MetricRow(string Key, long Step, DateTimeOffset At, double Value);
