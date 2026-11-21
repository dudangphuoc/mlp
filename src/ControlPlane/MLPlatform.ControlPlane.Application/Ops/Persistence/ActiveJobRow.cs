using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Timeline;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Notifications;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;
using MLPlatform.ControlPlane.Application.Scheduling;
using MLPlatform.Storage;

namespace MLPlatform.ControlPlane.Application.Ops;

public sealed record ActiveJobRow(Guid Id, string Name, string Kind, string State, string? StateReason, string? Progress, DateTimeOffset? StalledAt,
    string? AttemptState, DateTimeOffset? SignalLostAt, DateTimeOffset? SuspectUntil);
