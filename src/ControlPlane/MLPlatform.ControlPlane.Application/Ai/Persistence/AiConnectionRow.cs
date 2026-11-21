using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

// AI providers (Hermes phase 1).

public sealed record AiConnectionRow(Guid Id, string Name, string ProviderType, string Endpoint, string? SecretEnc, int MaxConcurrency, int RpmLimit,
    int TimeoutMs, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool IsExternal);
