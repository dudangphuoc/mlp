using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Labeling;

/// <summary>Progress of one project and the caller's own share of it.</summary>
public sealed record ProjectSummaryRow(Guid ProjectId, long Total, long Accepted, long InReview, long Unlabeled, long MyOpen, long MyReturned, string MyRoles);
