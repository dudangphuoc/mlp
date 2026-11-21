using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>A trial (round 1 R15–R22) with the revision number it tried and the job of its hidden run.</summary>
public sealed record TrialRow(Guid Id, Guid WorkspaceId, Guid ProjectId, Guid ProjectRecipeId, Guid RevisionId, Guid RunId, string TrialKey,
    Guid? DatasetVersionId, string StartKind, Guid? HostId, int Gpus, string State, string Stages, double? StepsPerS, long? PeakVramMb,
    bool VramEstimated, double? ValSPerBatch, int TrainImages, int ValImages, string Error, Guid CreatedBy, DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt, int RevisionNumber, Guid? JobId);
