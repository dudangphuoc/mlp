using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Data;

/// <summary>The train/val/test split a version carries, if it has been looked at.</summary>
public sealed record VersionSplitRow(Guid Id, string? SplitState, string? SplitCounts);
