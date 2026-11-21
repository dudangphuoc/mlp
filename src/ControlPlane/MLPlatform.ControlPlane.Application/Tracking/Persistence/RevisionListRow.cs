using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A revision as the history list shows it.</summary>
public sealed record RevisionListRow(int Number, string Variant, string Params, int? Parent, Guid CreatedBy, DateTimeOffset CreatedAt, long RunCount);
