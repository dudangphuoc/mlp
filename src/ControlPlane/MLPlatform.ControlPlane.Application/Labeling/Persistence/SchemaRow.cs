using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record SchemaRow(Guid Id, Guid DatasetId, int Version, string Task, string Classes, DateTimeOffset CreatedAt);
