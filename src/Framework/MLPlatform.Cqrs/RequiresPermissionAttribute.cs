namespace MLPlatform.Cqrs;

/// <summary>Declares the minimum permission for a command/query; AuthorizationBehavior reads it via
/// RequestContext.Permission.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
