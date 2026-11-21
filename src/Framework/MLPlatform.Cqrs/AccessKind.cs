namespace MLPlatform.Cqrs;

public enum AccessKind
{
    WorkspacePermission,
    ScopedPermission,
    InstanceAdmin,
    Authenticated,
    WorkloadToken,
    Internal,
    Anonymous,
}
