using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>One person's working copy of a project recipe plus what they want to train it on.</summary>
public sealed record DraftRow(Guid Id, Guid ProjectRecipeId, Guid UserId, Guid? BaseRevisionId, string Variant, string Params, Guid? DatasetVersionId,
    string StartKind, string TrainKind, Guid? RetrainOfRunId, Guid? HostId, int Gpus, double Cpus, string RunName, int Etag);
