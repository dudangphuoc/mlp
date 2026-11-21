using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Identity;

/// <summary>My API keys (token not included — shown only once at creation).</summary>
public sealed record ApiKeyRow(Guid Id, string? Name, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);
