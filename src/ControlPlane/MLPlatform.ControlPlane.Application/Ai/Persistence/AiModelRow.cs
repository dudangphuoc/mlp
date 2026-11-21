using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

public sealed record AiModelRow(Guid Id, Guid ConnectionId, string ModelId, string DisplayName, string[] Roles, string[] Inputs, string[] Outputs,
    string[] Skills, string Tier, double? InputUsdPer1m, double? OutputUsdPer1m, int MaxTokens, bool IsActive, string Health, string? HealthReason,
    DateTimeOffset? HealthAt, int ConsecutiveFailures, Guid? LastTestRunId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
