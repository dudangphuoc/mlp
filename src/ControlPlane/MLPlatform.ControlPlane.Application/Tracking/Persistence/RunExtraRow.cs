using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Tracking;

/// <summary>What a run cooked: revision number, how it started, which machine it was pinned to.</summary>
public sealed record RunExtraRow(Guid RunId, Guid? RevisionId, int? RevisionNumber, string? TrainKind, string? StartKind, int? RetrainOfNumber, Guid? HostId,
    string Origin, string? HostName, string? Code, int? ExitCode, DateTimeOffset? HeartbeatAt);
