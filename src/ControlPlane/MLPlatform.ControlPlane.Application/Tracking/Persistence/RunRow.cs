using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

public sealed record RunRow(Guid Id, Guid ProjectId, Guid WorkspaceId, string Name, string State, string Recipe, string Variant,
    string Task, Guid? JobId, string Params, string PrimaryMetricKey, double? PrimaryMetricValue, DateTimeOffset CreatedAt,
    int Number, Guid? DatasetVersionId, bool FromScratch, int Gpus, double Cpus, DateTimeOffset? EndedAt, Guid UserId);
