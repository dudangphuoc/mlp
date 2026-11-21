using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

public sealed record AiRunRow(Guid Id, Guid? WorkspaceId, string Kind, string Purpose, string? Subject, string Trigger, Guid? ActorId, string? ActorName, string State,
    string? Fallback, Guid? ModelRef, string? ModelLabel, string SelectionJson, string InputJson, string OutputJson, string? DecidedBy, double? Confidence,
    int? LatencyMs, int? TokensIn, int? TokensOut, double? CostUsd, string AttemptsJson, string? ErrorCode, string? Error, DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
