using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Identity;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Platform;

/// <summary>Workspace slug ↔ id (public subdomain routing).</summary>
public sealed record WorkspaceSlugRow(Guid Id, string Slug);
