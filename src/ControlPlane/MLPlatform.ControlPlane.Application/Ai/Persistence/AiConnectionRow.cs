using System.Text.Json.Nodes;
using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.ControlPlane.Application.Platform;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Ai;

/*
 * AI providers (Hermes phase 1). Connections (endpoint + encrypted key + limits) hold declared models (capabilities, roles,
 * tier, price). One explicit default model per role. Every AI call is an ai.runs row so the UI can show it — nothing runs unseen.
 * Instance-wide; managed by the Instance Admin. Calling providers and encrypting keys happens in the api (AiRuntime).
 */

public sealed record AiConnectionRow(Guid Id, string Name, string ProviderType, string Endpoint, string? SecretEnc, int MaxConcurrency, int RpmLimit,
    int TimeoutMs, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool IsExternal);
