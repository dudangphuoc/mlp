using MLPlatform.Cqrs;
using MLPlatform.Data;

namespace MLPlatform.ControlPlane.Application.Common;

/// <summary>Permission matrix M01 §4.1 (implemented part).</summary>
public static class Permissions
{
    private static readonly Dictionary<string, string[]> Matrix = new()
    {
        ["workspace.manage"] = ["owner"],
        ["member.manage"] = ["owner", "admin"],
        ["fleet.manage"] = ["owner", "admin"],
        ["fleet.read"] = ["owner", "admin", "editor", "viewer"],
        ["dataset.write"] = ["owner", "admin", "editor"],
        ["dataset.read"] = ["owner", "admin", "editor", "reviewer", "annotator", "viewer"],
        ["job.submit"] = ["owner", "admin", "editor"],
        ["model.write"] = ["owner", "admin", "editor"],
        ["model.promote_production"] = ["owner", "admin"],
        ["run.read"] = ["owner", "admin", "editor", "viewer"],
        ["deployment.manage"] = ["owner", "admin", "editor"],
        ["audit.read"] = ["owner", "admin"],
        ["notebook.use"] = ["owner", "admin", "editor"],
        ["notebook.admin"] = ["owner", "admin"],
        ["labeling.manage"] = ["owner", "admin", "editor"],
        ["labeling.annotate"] = ["owner", "admin", "editor", "reviewer", "annotator"],
    };

    public static bool Allows(string role, string permission) =>
        Matrix.TryGetValue(permission, out var roles) && roles.Contains(role);
}
