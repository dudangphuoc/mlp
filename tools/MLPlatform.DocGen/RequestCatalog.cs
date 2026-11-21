using MLPlatform.ControlPlane.Application.Common;
using MLPlatform.ControlPlane.Application.Generated;

namespace MLPlatform.DocGen;

/// <summary>Requests from the CQRS generator's catalog, with the roles that hold each permission.</summary>
public static class RequestCatalog
{
    public static IReadOnlyList<RequestInfo> Load() =>
        [.. CqrsRegistration.Requests
            .Select(r => new RequestInfo(r.Name, r.IsCommand, r.Access, r.Permission, RolesFor(r.Permission)))
            .OrderBy(r => r.FullName, StringComparer.Ordinal)];

    private static IReadOnlyList<string> RolesFor(string? permission) =>
        permission is not null && Permissions.RolesByPermission.TryGetValue(permission, out var roles)
            ? [.. RoleOrder.Where(roles.Contains)]
            : [];

    private static readonly string[] RoleOrder = ["owner", "admin", "editor", "reviewer", "annotator", "viewer"];
}
