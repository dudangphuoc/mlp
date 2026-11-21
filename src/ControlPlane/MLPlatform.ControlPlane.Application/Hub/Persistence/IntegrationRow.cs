using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Data;
using MLPlatform.ControlPlane.Application.Registry;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Hub;

/// <summary>Workspace integration (secret is encrypted — only the api decrypts it when calling HF).</summary>
public sealed record IntegrationRow(Guid WorkspaceId, string Kind, string SecretEnc, string? Account, string Status, DateTimeOffset UpdatedAt, DateTimeOffset? LastCheckedAt);
