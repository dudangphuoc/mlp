namespace MLPlatform.Cqrs;

/// <summary>Who may send a request when it is not a plain workspace permission (that one is <see cref="RequiresPermissionAttribute"/>).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AccessAttribute(AccessKind kind, string? permission = null) : Attribute
{
    public AccessKind Kind { get; } = kind;

    public string? Permission { get; } = permission;
}
