using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>Run counts and the best result of a project, for the project list.</summary>
public sealed record ProjectStatsRow(Guid ProjectId, long RunCount, long ActiveCount, DateTimeOffset? LastRunAt, int? BestNumber, string? BestName,
    string? BestMetricKey, double? BestMetricValue);
