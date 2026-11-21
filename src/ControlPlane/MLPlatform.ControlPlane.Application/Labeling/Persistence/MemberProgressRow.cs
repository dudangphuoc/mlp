using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>What one annotator has delivered in a project.</summary>
public sealed record MemberProgressRow(Guid UserId, long Submitted, long Accepted, long Returned, DateTimeOffset? LastAt);
