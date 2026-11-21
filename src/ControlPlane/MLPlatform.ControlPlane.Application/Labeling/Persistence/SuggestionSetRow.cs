using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record SuggestionSetRow(Guid Id, Guid ProjectId, string Path, int Revision, string SourceKind, Guid SourceId, string SourceLabel, bool External,
    string State, string Detail, string ItemsJson, string UnmappedJson, Guid? AiRunId, int LatencyMs, Guid? JobId, string Decision, string? CreatedByName,
    DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt);
