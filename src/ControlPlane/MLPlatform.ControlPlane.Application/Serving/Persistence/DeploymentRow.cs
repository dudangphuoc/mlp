using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Serving;

public sealed record DeploymentRow(Guid Id, Guid WorkspaceId, string Slug, Guid ModelVersionId, string ModelName, int ModelVersion, string Task,
    string State, string? StateReason, Guid? JobId, string? Endpoint, DateTimeOffset CreatedAt,
    string Purpose, int? IdleStopMin, DateTimeOffset? LastUsedAt, double? Cpus, int? Gpus, string? ImageOverride);
