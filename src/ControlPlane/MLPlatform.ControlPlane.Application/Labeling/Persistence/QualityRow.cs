using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

/*
 * Hermes phase 4: annotator quality and calibration. Everything is computed from what already happened — submits, reviewer
 * decisions, Hermes checks — so each number can be traced back to items and checks.
 */

public sealed record QualityRow(Guid AnnotatorId, string? AnnotatorName, long Items, long Submitted, long Accepted, long Rejected, long InReview,
    long ReturnedEver, long Checked, long HermesPass, long HermesFlag, long HermesFail, long AutoAccepted, long Undone, double? AvgConfidence);
