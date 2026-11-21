using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

// Hermes label checks (phase 2).

public sealed record HermesCheckRow(Guid Id, Guid ProjectId, string Path, int Revision, string Trigger, string Verdict, double? Confidence, string DecidedBy,
    string Summary, string StepsJson, string? Suggestion, Guid? AiRunId, string Action, Guid? ActorId, string? ActorName, DateTimeOffset CreatedAt,
    Guid? UndoneBy, string? UndoneByName, DateTimeOffset? UndoneAt, string? Explanation, string? ExplanationLang, Guid? ExplanationRunId,
    DateTimeOffset? ExplainedAt);
