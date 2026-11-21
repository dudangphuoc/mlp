using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.Cqrs;
using MLPlatform.Data;
using MLPlatform.Security;

namespace MLPlatform.ControlPlane.Application.Fleet;

// ---- Agent enroll over the :7443 channel (no user — authenticated by an mlpe_ token) ----
public sealed record AgentEnrollResult(Guid HostId, Guid WorkspaceId, string Credential, string OverlayIp);
