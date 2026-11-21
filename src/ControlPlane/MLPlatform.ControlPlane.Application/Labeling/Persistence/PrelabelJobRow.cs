using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Labeling;

public sealed record PrelabelJobRow(Guid Id, Guid ProjectId, string SourceKind, Guid SourceId, string SourceLabel, int Total, int Done, int Failed, string State,
    string? Error, string? CreatedByName, Guid CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);
