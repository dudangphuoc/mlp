using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>One metric of a run without its points: how many, the last step and value, the range.</summary>
public sealed record MetricSummaryRow(string Key, long Points, long LastStep, double MinValue, double MaxValue, double LastValue);
